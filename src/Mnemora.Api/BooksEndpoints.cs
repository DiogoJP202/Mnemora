using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class BooksEndpoints
{
    public static IEndpointRouteBuilder MapBooksEndpoints(this IEndpointRouteBuilder app)
    {
        var books = app.MapGroup("/api/books").WithTags("Books");

        books.MapGet("/", async (MnemoraDbContext db) =>
        {
            var rows = await db.Books.AsNoTracking()
                .Where(x => x.CatalogKind == BookCatalogKind.Curated)
                .OrderBy(x => x.Title).Take(100)
                .Select(x => new BookDto(x.Id, x.Title, x.Author, x.Description, x.CoverUrl,
                    db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                .ToListAsync();
            return Results.Ok(rows);
        });

        books.MapGet("/search", async (
            string? q, ClaimsPrincipal user, MnemoraDbContext db,
            IBookMetadataProvider provider, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var query = q?.Trim();
            if (string.IsNullOrWhiteSpace(query)) return Results.Ok(Array.Empty<BookSearchDto>());
            if (query.Length > 100) return Results.BadRequest("Busca muito longa.");
            var term = query.ToLowerInvariant();
            var isbnTerm = NormalizeIsbnSearch(query);
            var userId = TryUserId(user);
            var local = await db.Books.AsNoTracking().Where(x =>
                    (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId)
                    && (x.Title.ToLower().Contains(term) || x.Author.ToLower().Contains(term)
                        || (x.Isbn10 != null && x.Isbn10.Contains(term))
                        || (x.Isbn13 != null && x.Isbn13.Contains(term))
                        || (isbnTerm != null && x.Isbn10 != null && x.Isbn10 == isbnTerm)
                        || (isbnTerm != null && x.Isbn13 != null && x.Isbn13 == isbnTerm)))
                .OrderBy(x => x.Title).Take(20)
                .Select(x => new BookSearchDto(x.Id, x.ExternalProvider, x.ExternalId, "local",
                    x.Title, x.Author, x.CoverUrl,
                    db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                .ToListAsync(cancellationToken);
            if (!provider.IsConfigured) return Results.Ok(local);
            try
            {
                var external = await provider.SearchAsync(query, cancellationToken);
                var externalProviders = external.Select(x => x.Provider).Distinct().ToList();
                var externalIds = external.Select(x => x.ExternalId).Distinct().ToList();
                var knownExternalBooks = await db.Books.AsNoTracking()
                    .Where(x => x.ExternalProvider != null && x.ExternalId != null
                        && (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId)
                        && externalProviders.Contains(x.ExternalProvider)
                        && externalIds.Contains(x.ExternalId))
                    .Select(x => new BookSearchDto(x.Id, x.ExternalProvider, x.ExternalId,
                        "local", x.Title, x.Author, x.CoverUrl,
                        db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                    .ToListAsync(cancellationToken);
                var knownByKey = knownExternalBooks.ToDictionary(x =>
                    ExternalKey(x.ExternalProvider!, x.ExternalId!),
                    StringComparer.OrdinalIgnoreCase);
                var includedBookIds = local.Where(x => x.Id.HasValue)
                    .Select(x => x.Id!.Value).ToHashSet();
                foreach (var item in external)
                {
                    if (knownByKey.TryGetValue(ExternalKey(item.Provider, item.ExternalId),
                            out var known))
                    {
                        if (known.Id is Guid knownId && includedBookIds.Add(knownId))
                            local.Add(known);
                        continue;
                    }
                    local.Add(new BookSearchDto(null, item.Provider, item.ExternalId, "external",
                        item.Title, item.Author, item.CoverUrl, false));
                }
                return Results.Ok(local);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                loggerFactory.CreateLogger("Mnemora.BookCatalog")
                    .LogWarning(exception, "Falha no provider de metadados de livros.");
                return local.Count > 0 ? Results.Ok(local)
                    : Results.Problem("Não foi possível buscar livros externos agora.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("catalog-external");

        books.MapGet("/{bookId:guid}", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = TryUserId(user);
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == bookId
                && (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId));
            if (book is null) return Results.NotFound();
            var memoryPackAvailable = await db.ReadingUnits.AsNoTracking()
                .AnyAsync(x => x.BookId == book.Id);
            return Results.Ok(CatalogDto.From(book, memoryPackAvailable));
        });

        books.MapGet("/{bookId:guid}/reading-units", async (
            Guid bookId, System.Security.Claims.ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            var library = await db.UserBooks.AsNoTracking().FirstAsync(x =>
                x.UserId == scope.UserId && x.BookId == bookId);
            var units = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == bookId)
                .OrderBy(x => x.OrderIndex).ToListAsync();
            return Results.Ok(units.Select(x =>
            {
                var known = KnowledgeVisibility.CanSee(scope.CurrentOrder, x.OrderIndex);
                return new ReadingUnitDto(x.Id, x.ParentUnitId, x.SafeLabel,
                    known ? x.Title : x.SafeLabel, x.Type.ToString(), x.OrderIndex,
                    known, library.CurrentReadingUnitId == x.Id);
            }));
        }).RequireAuthorization();

        books.MapGet("/{bookId:guid}/overview", async (
            Guid bookId, System.Security.Claims.ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            var book = await db.Books.AsNoTracking().FirstAsync(x => x.Id == bookId);
            var memoryPackAvailable = await db.ReadingUnits.AsNoTracking()
                .AnyAsync(x => x.BookId == bookId);
            return Results.Ok(new
            {
                book = CatalogDto.From(book, memoryPackAvailable),
                knownEntities = await reader.KnownEntities(scope).CountAsync(),
                knownEvents = await reader.KnownEntities(scope)
                    .CountAsync(x => x.Type == LoreEntityType.Event)
            });
        }).RequireAuthorization();

        return app;
    }

    private static Guid? TryUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId : null;

    private static string ExternalKey(string provider, string externalId) =>
        $"{provider}\u001f{externalId}";

    private static string? NormalizeIsbnSearch(string query)
    {
        var compact = new string(query.Where(character =>
            character is not '-' and not ' ' && character is not '\t').ToArray())
            .ToUpperInvariant();
        return compact.Length is 10 or 13
            && compact.All(character => character is >= '0' and <= '9' || character == 'X')
                ? compact : null;
    }
}
