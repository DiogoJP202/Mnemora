using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class LibraryEndpoints
{
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
            if (!await db.Books.AnyAsync(x => x.Id == bookId)) return Results.NotFound();
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
            if (string.IsNullOrWhiteSpace(request.ExternalId))
                return Results.BadRequest("Identificador externo inválido.");
            ExternalBookMetadata? metadata;
            try { metadata = await provider.GetAsync(request.ExternalId, cancellationToken); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return Results.Problem("Não foi possível importar o livro agora.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (metadata is null) return Results.NotFound();
            var book = await db.Books.FirstOrDefaultAsync(x =>
                x.ExternalProvider == "GoogleBooks" && x.ExternalId == metadata.ExternalId);
            if (book is null)
            {
                book = new Book
                {
                    Title = Limit(metadata.Title, 300), Author = Limit(metadata.Author, 300),
                    CoverUrl = metadata.CoverUrl, Isbn10 = metadata.Isbn10,
                    Isbn13 = metadata.Isbn13, Publisher = metadata.Publisher,
                    PublishedDate = metadata.PublishedDate, Language = metadata.Language,
                    ExternalProvider = "GoogleBooks", ExternalId = metadata.ExternalId,
                    // External descriptions may contain spoilers, so they are never imported.
                    Description = null
                };
                db.Books.Add(book);
                try { await db.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    book = await db.Books.FirstOrDefaultAsync(x =>
                        x.ExternalProvider == "GoogleBooks"
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
            if (unitId.HasValue)
            {
                row.Status = UserBookStatus.Reading;
                row.StartedAt ??= DateTime.UtcNow;
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
        return new LibraryBookDto(CatalogDto.From(book), row.Status.ToString(),
            row.CurrentReadingUnitId, row.CurrentPage, percentage);
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];
    private sealed record ExternalAddRequest(string ExternalId);
    private sealed record StatusWrite(string Status);
}
