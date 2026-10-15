using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class LibraryEndpoints
{
    private const string UnknownAuthor = "Autor não informado";
    private const int MaxPrivateBooksPerUser = 500;
    private static readonly SemaphoreSlim ManualBookCreateGate = new(1, 1);

    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var library = app.MapGroup("/api/library").RequireAuthorization().WithTags("Library");

        library.MapGet("", async (ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var rows = await db.UserBooks.AsNoTracking().Where(x => x.UserId == userId)
                .OrderByDescending(x => x.UpdatedAt).ToListAsync();
            var result = new List<LibraryBookDto>();
            foreach (var row in rows) result.Add(await ToDtoAsync(db, row));
            return Results.Ok(result);
        });

        library.MapPost("/{bookId:guid}", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            if (!await db.Books.AnyAsync(x => x.Id == bookId
                    && (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId)))
                return Results.NotFound();
            var existing = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == bookId);
            if (existing is not null) return Results.Ok(await ToDtoAsync(db, existing));
            var row = new UserBook { UserId = userId, BookId = bookId };
            db.UserBooks.Add(row);
            await db.SaveChangesAsync();
            return Results.Created($"/api/library/{bookId}", await ToDtoAsync(db, row));
        });

        library.MapPost("/external", async (
            ExternalBookImportRequest request, ClaimsPrincipal user, MnemoraDbContext db,
            ExternalBookImporter importer, CancellationToken cancellationToken) =>
        {
            Book book;
            try { book = await importer.ImportAsync(request.ExternalProvider, request.ExternalId, cancellationToken); }
            catch (BookImportException exception)
            {
                return exception.StatusCode == 404 ? Results.NotFound()
                    : Results.Problem(detail: exception.Message, statusCode: exception.StatusCode);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return Results.Problem("Não foi possível importar o livro agora.", statusCode: 503);
            }
            var userId = CurrentUser.Id(user);
            var libraryBook = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == book.Id, cancellationToken);
            if (libraryBook is null)
            {
                libraryBook = new UserBook { UserId = userId, BookId = book.Id };
                db.UserBooks.Add(libraryBook);
                try { await db.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    libraryBook = await db.UserBooks.FirstOrDefaultAsync(x =>
                        x.UserId == userId && x.BookId == book.Id, cancellationToken);
                    if (libraryBook is null) throw;
                }
            }
            return Results.Ok(await ToDtoAsync(db, libraryBook));
        }).RequireRateLimiting("import-external");

        library.MapPost("/manual", async (
            ManualBookRequest request, ClaimsPrincipal user, MnemoraDbContext db,
            CancellationToken cancellationToken) =>
        {
            var title = NormalizeText(request.Title);
            if (title is null || title.Length > 300)
                return Results.BadRequest("O título deve ter entre 1 e 300 caracteres.");

            var author = NormalizeText(request.Author) ?? UnknownAuthor;
            if (author.Length > 300)
                return Results.BadRequest("O autor deve ter no máximo 300 caracteres.");

            var isbn = BookIsbn.Normalize(request.Isbn);
            if (!isbn.IsValid)
                return Results.BadRequest("ISBN inválido. Informe um ISBN-10 ou ISBN-13 válido.");

            var userId = CurrentUser.Id(user);
            await ManualBookCreateGate.WaitAsync(cancellationToken);
            try
            {
                var ownedBooks = db.Books.Where(x =>
                    x.CatalogKind == BookCatalogKind.Private && x.OwnerUserId == userId);
                Book? book;
                if (isbn.Isbn13 is not null)
                    book = await ownedBooks.FirstOrDefaultAsync(x =>
                        x.Isbn13 == isbn.Isbn13, cancellationToken);
                else if (isbn.Isbn10 is not null)
                    book = await ownedBooks.FirstOrDefaultAsync(x =>
                        x.Isbn10 == isbn.Isbn10, cancellationToken);
                else
                {
                    var titleKey = title.ToLowerInvariant();
                    var authorKey = author.ToLowerInvariant();
                    book = await ownedBooks.FirstOrDefaultAsync(x =>
                        x.Title.ToLower() == titleKey && x.Author.ToLower() == authorKey,
                        cancellationToken);
                }

                if (book is null && (isbn.Isbn10 is not null || isbn.Isbn13 is not null))
                {
                    var titleKey = title.ToLowerInvariant();
                    var authorKey = author.ToLowerInvariant();
                    book = await ownedBooks.FirstOrDefaultAsync(x =>
                        x.Isbn10 == null && x.Isbn13 == null
                        && x.Title.ToLower() == titleKey && x.Author.ToLower() == authorKey,
                        cancellationToken);
                    if (book is not null)
                    {
                        book.Isbn10 = isbn.Isbn10;
                        book.Isbn13 = isbn.Isbn13;
                        book.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                if (book is null)
                {
                    if (await ownedBooks.CountAsync(cancellationToken) >= MaxPrivateBooksPerUser)
                        return Results.Problem(
                            $"O limite de {MaxPrivateBooksPerUser} livros manuais foi atingido.",
                            statusCode: StatusCodes.Status409Conflict);
                    book = new Book
                    {
                        Title = title,
                        Author = author,
                        Isbn10 = isbn.Isbn10,
                        Isbn13 = isbn.Isbn13,
                        CatalogKind = BookCatalogKind.Private,
                        OwnerUserId = userId,
                        ReadingOutlineStatus = EditorialContentStatus.None,
                        LoreStatus = EditorialContentStatus.None
                    };
                    db.Books.Add(book);
                    await db.SaveChangesAsync(cancellationToken);
                }

                var libraryBook = await db.UserBooks.FirstOrDefaultAsync(x =>
                    x.UserId == userId && x.BookId == book.Id, cancellationToken);
                if (libraryBook is null)
                {
                    libraryBook = new UserBook { UserId = userId, BookId = book.Id };
                    db.UserBooks.Add(libraryBook);
                    await db.SaveChangesAsync(cancellationToken);
                }

                return Results.Ok(await ToDtoAsync(db, libraryBook));
            }
            finally
            {
                ManualBookCreateGate.Release();
            }
        }).RequireRateLimiting("import-external");

        library.MapDelete("/{bookId:guid}", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = CurrentUser.Id(user);
            await using var transaction = await db.Database
                .BeginTransactionAsync(cancellationToken);
            if (!await db.UserBooks.AsNoTracking().AnyAsync(x =>
                    x.UserId == userId && x.BookId == bookId, cancellationToken))
                return Results.NotFound();

            var privateBookCanBeRemoved = await db.Books.AsNoTracking().AnyAsync(x =>
                    x.Id == bookId
                    && x.CatalogKind == BookCatalogKind.Private
                    && x.OwnerUserId == userId, cancellationToken)
                && !await db.ReadingUnits.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId, cancellationToken)
                && !await db.LoreEntities.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId, cancellationToken)
                && !await db.LoreRelations.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId, cancellationToken)
                && !await db.UserBooks.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId && x.UserId != userId, cancellationToken)
                && !await db.UserNotes.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId && x.UserId != userId, cancellationToken)
                && !await db.UserRecallActivities.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId && x.UserId != userId, cancellationToken);

            await db.UserNotes.Where(x => x.UserId == userId && x.BookId == bookId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.UserRecallActivities.Where(x =>
                    x.UserId == userId && x.BookId == bookId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.MemoryPackRequests.Where(x =>
                    x.UserId == userId && x.BookId == bookId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.UserBooks.Where(x => x.UserId == userId && x.BookId == bookId)
                .ExecuteDeleteAsync(cancellationToken);

            if (privateBookCanBeRemoved)
                await db.Books.Where(x => x.Id == bookId)
                    .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("memory-write");

        library.MapPatch("/{bookId:guid}/progress", async (
            Guid bookId, JsonElement request, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var row = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == bookId);
            if (row is null) return Results.NotFound();
            if (request.ValueKind != JsonValueKind.Object)
                return Results.BadRequest("Progresso inválido.");
            var hasUnit = request.TryGetProperty("currentReadingUnitId", out var unitValue);
            var hasPage = request.TryGetProperty("currentPage", out var pageValue);
            var hasStatus = request.TryGetProperty("status", out var statusValue);
            if (!hasUnit && !hasPage && !hasStatus)
                return Results.BadRequest("Informe unidade, página ou status.");
            Guid? unitId = row.CurrentReadingUnitId;
            if (hasUnit)
            {
                if (unitValue.ValueKind == JsonValueKind.Null) unitId = null;
                else if (unitValue.ValueKind == JsonValueKind.String
                    && Guid.TryParse(unitValue.GetString(), out var parsed)) unitId = parsed;
                else return Results.BadRequest("Unidade de leitura inválida.");
            }
            int? currentPage = row.CurrentPage;
            if (hasPage)
            {
                if (pageValue.ValueKind == JsonValueKind.Null) currentPage = null;
                else if (pageValue.ValueKind == JsonValueKind.Number
                    && pageValue.TryGetInt32(out var parsed)) currentPage = parsed;
                else return Results.BadRequest("Página de referência inválida.");
            }
            UserBookStatus? explicitStatus = null;
            if (hasStatus)
            {
                if (statusValue.ValueKind != JsonValueKind.String
                    || !TryParseStatus(statusValue.GetString(), out var parsed))
                    return Results.BadRequest("Status inválido.");
                explicitStatus = parsed;
            }
            if (currentPage is <= 0)
                return Results.BadRequest("Página de referência inválida.");
            if (hasUnit && unitId is Guid selectedUnit)
            {
                var outlinePublished = await db.Books.AsNoTracking().AnyAsync(x =>
                    x.Id == bookId
                    && x.ReadingOutlineStatus == EditorialContentStatus.Published);
                if (!outlinePublished || !await db.ReadingUnits.AnyAsync(x =>
                        x.Id == selectedUnit && x.BookId == bookId))
                    return Results.BadRequest("Unidade de leitura inválida para este livro.");
            }
            var unitChanged = hasUnit && unitId != row.CurrentReadingUnitId;
            var pageChanged = hasPage && currentPage != row.CurrentPage;
            row.CurrentReadingUnitId = unitId;
            row.CurrentPage = currentPage;
            var updatedAt = DateTime.UtcNow;
            row.UpdatedAt = updatedAt;
            if (explicitStatus is UserBookStatus status)
            {
                ApplyStatus(row, status, updatedAt);
            }
            else if (hasUnit && unitId.HasValue)
            {
                row.Status = UserBookStatus.Reading;
                row.StartedAt ??= updatedAt;
            }
            if ((unitChanged && unitId.HasValue) || (pageChanged && currentPage.HasValue))
            {
                row.StartedAt ??= updatedAt;
                row.LastReadAt = updatedAt;
            }
            await db.SaveChangesAsync();
            return Results.Ok(await ToDtoAsync(db, row));
        }).RequireRateLimiting("memory-write");

        library.MapPatch("/{bookId:guid}/status", async (
            Guid bookId, StatusWrite request, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var row = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == bookId);
            if (row is null) return Results.NotFound();
            if (!TryParseStatus(request.Status, out var status))
                return Results.BadRequest("Status inválido.");
            var updatedAt = DateTime.UtcNow;
            ApplyStatus(row, status, updatedAt);
            row.UpdatedAt = updatedAt;
            await db.SaveChangesAsync();
            return Results.Ok(await ToDtoAsync(db, row));
        }).RequireRateLimiting("memory-write");

        return app;
    }

    private static async Task<LibraryBookDto> ToDtoAsync(MnemoraDbContext db, UserBook row)
    {
        var book = await db.Books.AsNoTracking().FirstAsync(x => x.Id == row.BookId);
        var unitIds = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == row.BookId)
            .OrderBy(x => x.OrderIndex).Select(x => x.Id).ToListAsync();
        var hasLore = await db.LoreEntities.AsNoTracking()
            .AnyAsync(x => x.BookId == row.BookId);
        var requested = await db.MemoryPackRequests.AsNoTracking().AnyAsync(x =>
            x.BookId == row.BookId && x.UserId == row.UserId);
        var index = row.CurrentReadingUnitId is Guid current
            ? unitIds.IndexOf(current) : -1;
        var percentage = index < 0 || unitIds.Count == 0
            ? 0 : (int)Math.Round(100.0 * (index + 1) / unitIds.Count);
        return new LibraryBookDto(CatalogDto.From(
                book, unitIds.Count > 0, hasLore, requested), row.Status.ToString(),
            row.CurrentReadingUnitId, row.CurrentPage, percentage);
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? LimitOptional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Limit(value, max);

    private static bool TryParseStatus(string? value, out UserBookStatus status)
    {
        status = default;
        return !string.IsNullOrWhiteSpace(value)
            && value.All(char.IsLetter)
            && Enum.TryParse(value, true, out status)
            && Enum.IsDefined(status);
    }

    private static void ApplyStatus(UserBook row, UserBookStatus status, DateTime updatedAt)
    {
        row.Status = status;
        row.StartedAt ??= status == UserBookStatus.Reading ? updatedAt : null;
        row.FinishedAt = status == UserBookStatus.Finished
            ? row.FinishedAt ?? updatedAt
            : null;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ManualBookRequest(string? Title, string? Author, string? Isbn);
    private sealed record StatusWrite(string Status);
}
