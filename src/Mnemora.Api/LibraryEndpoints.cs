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
            ExternalAddRequest request, ClaimsPrincipal user, MnemoraDbContext db,
            IBookMetadataProvider provider, CancellationToken cancellationToken) =>
        {
            if (!provider.IsConfigured)
                return Results.Problem("Busca externa não está configurada.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            if (string.IsNullOrWhiteSpace(request.ExternalProvider)
                || request.ExternalProvider.Length > 80
                || string.IsNullOrWhiteSpace(request.ExternalId)
                || request.ExternalId.Length > 200)
                return Results.BadRequest("Identificador externo inválido.");
            ExternalBookMetadata? metadata;
            try
            {
                metadata = await provider.GetAsync(request.ExternalProvider.Trim(),
                    request.ExternalId.Trim(), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return Results.Problem("Não foi possível importar o livro agora.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (metadata is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(metadata.Provider)
                || string.IsNullOrWhiteSpace(metadata.ExternalId))
                return Results.Problem("O catálogo externo retornou dados inválidos.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            var book = await db.Books.FirstOrDefaultAsync(x =>
                x.ExternalProvider == metadata.Provider && x.ExternalId == metadata.ExternalId);
            if (book is null)
            {
                book = new Book
                {
                    Title = Limit(metadata.Title, 300), Author = Limit(metadata.Author, 300),
                    CoverUrl = metadata.CoverUrl, Isbn10 = metadata.Isbn10,
                    Isbn13 = metadata.Isbn13, Publisher = metadata.Publisher,
                    PublishedDate = metadata.PublishedDate, Language = metadata.Language,
                    ExternalProvider = Limit(metadata.Provider, 80),
                    ExternalId = Limit(metadata.ExternalId, 200),
                    CatalogKind = BookCatalogKind.Imported,
                    // External descriptions may contain spoilers, so they are never imported.
                    Description = null
                };
                db.Books.Add(book);
                try { await db.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    book = await db.Books.FirstOrDefaultAsync(x =>
                        x.ExternalProvider == metadata.Provider
                        && x.ExternalId == metadata.ExternalId, cancellationToken);
                    if (book is null) throw;
                }
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

            var isbn = NormalizeIsbn(request.Isbn);
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
                        OwnerUserId = userId
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
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var row = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == bookId);
            if (row is null) return Results.NotFound();
            db.UserBooks.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

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
            if (!hasUnit && !hasPage)
                return Results.BadRequest("Informe unidade ou página.");
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
            if (currentPage is <= 0)
                return Results.BadRequest("Página de referência inválida.");
            if (unitId is Guid selectedUnit
                && !await db.ReadingUnits.AnyAsync(x => x.Id == selectedUnit && x.BookId == bookId))
                return Results.BadRequest("Unidade de leitura inválida para este livro.");
            row.CurrentReadingUnitId = unitId;
            row.CurrentPage = currentPage;
            row.UpdatedAt = DateTime.UtcNow;
            if (hasUnit && unitId.HasValue)
            {
                row.Status = UserBookStatus.Reading;
                row.StartedAt ??= DateTime.UtcNow;
            }
            if ((hasUnit && unitId.HasValue) || (hasPage && currentPage.HasValue))
            {
                row.LastReadAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(await ToDtoAsync(db, row));
        });

        library.MapPatch("/{bookId:guid}/status", async (
            Guid bookId, StatusWrite request, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var row = await db.UserBooks.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.BookId == bookId);
            if (row is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Status)
                || !request.Status.All(char.IsLetter)
                || !Enum.TryParse<UserBookStatus>(request.Status, true, out var status)
                || !Enum.IsDefined(status))
                return Results.BadRequest("Status inválido.");
            row.Status = status;
            row.StartedAt ??= status == UserBookStatus.Reading ? DateTime.UtcNow : null;
            row.FinishedAt = status == UserBookStatus.Finished ? DateTime.UtcNow : null;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToDtoAsync(db, row));
        });

        return app;
    }

    private static async Task<LibraryBookDto> ToDtoAsync(MnemoraDbContext db, UserBook row)
    {
        var book = await db.Books.AsNoTracking().FirstAsync(x => x.Id == row.BookId);
        var unitIds = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == row.BookId)
            .OrderBy(x => x.OrderIndex).Select(x => x.Id).ToListAsync();
        var index = row.CurrentReadingUnitId is Guid current
            ? unitIds.IndexOf(current) : -1;
        var percentage = index < 0 || unitIds.Count == 0
            ? 0 : (int)Math.Round(100.0 * (index + 1) / unitIds.Count);
        return new LibraryBookDto(CatalogDto.From(book, unitIds.Count > 0), row.Status.ToString(),
            row.CurrentReadingUnitId, row.CurrentPage, percentage);
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static NormalizedIsbn NormalizeIsbn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new(true, null, null);
        if (value.Length > 32) return new(false, null, null);
        var compact = new string(value.Where(x => !char.IsWhiteSpace(x) && x != '-').ToArray())
            .ToUpperInvariant();
        if (compact.Length == 10 && IsValidIsbn10(compact))
            return new(true, compact, ToIsbn13(compact));
        if (compact.Length == 13 && IsValidIsbn13(compact))
            return new(true, compact.StartsWith("978", StringComparison.Ordinal)
                ? ToIsbn10(compact) : null, compact);
        return new(false, null, null);
    }

    private static bool IsValidIsbn10(string value)
    {
        if (value.Length != 10 || value[..9].Any(x => !char.IsDigit(x))
            || (!char.IsDigit(value[9]) && value[9] != 'X'))
            return false;
        var sum = 0;
        for (var index = 0; index < 10; index++)
        {
            var digit = index == 9 && value[index] == 'X' ? 10 : value[index] - '0';
            sum += (10 - index) * digit;
        }
        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string value)
    {
        if (value.Length != 13 || value.Any(x => !char.IsDigit(x))) return false;
        var sum = 0;
        for (var index = 0; index < 12; index++)
            sum += (value[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10 == value[12] - '0';
    }

    private static string ToIsbn13(string isbn10)
    {
        var body = $"978{isbn10[..9]}";
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
            sum += (body[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return $"{body}{(10 - sum % 10) % 10}";
    }

    private static string ToIsbn10(string isbn13)
    {
        var body = isbn13.Substring(3, 9);
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
            sum += (body[index] - '0') * (10 - index);
        var check = (11 - sum % 11) % 11;
        return $"{body}{(check == 10 ? 'X' : (char)('0' + check))}";
    }

    private sealed record ExternalAddRequest(string ExternalProvider, string ExternalId);
    private sealed record ManualBookRequest(string? Title, string? Author, string? Isbn);
    private sealed record NormalizedIsbn(bool IsValid, string? Isbn10, string? Isbn13);
    private sealed record StatusWrite(string Status);
}
