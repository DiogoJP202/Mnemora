using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
                .Select(x => new BookDto(x.Id, x.Title, x.Subtitle, x.Author,
                    x.Description, x.CoverUrl, x.Isbn10, x.Isbn13,
                    x.Publisher, x.PublishedDate, x.Language,
                    db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                .ToListAsync();
            return Results.Ok(rows);
        });

        books.MapGet("/search", async (
            string? q, ClaimsPrincipal user, MnemoraDbContext db,
            IBookMetadataProvider provider, IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var query = q?.Trim();
            if (string.IsNullOrWhiteSpace(query)) return Results.Ok(Array.Empty<BookSearchDto>());
            if (query.Length > 100) return Results.BadRequest("Busca muito longa.");
            var term = query.ToLowerInvariant();
            var isbn = BookIsbn.Normalize(query);
            var userId = TryUserId(user);
            var local = await db.Books.AsNoTracking().Where(x =>
                    (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId)
                    && (x.Title.ToLower().Contains(term)
                        || (x.Subtitle != null && x.Subtitle.ToLower().Contains(term))
                        || x.Author.ToLower().Contains(term)
                        || (x.Isbn10 != null && x.Isbn10.Contains(term))
                        || (x.Isbn13 != null && x.Isbn13.Contains(term))
                        || (isbn.Isbn10 != null && x.Isbn10 == isbn.Isbn10)
                        || (isbn.Isbn13 != null && x.Isbn13 == isbn.Isbn13)))
                .OrderBy(x => x.Title).Take(20)
                .Select(x => new BookSearchDto(x.Id, x.ExternalProvider, x.ExternalId, "local",
                    x.Title, x.Subtitle, x.Author, x.CoverUrl,
                    x.Isbn10, x.Isbn13, x.Publisher, x.PublishedDate, x.Language,
                    null, null, null,
                    db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                .ToListAsync(cancellationToken);
            if (!provider.IsConfigured) return Results.Ok(local);
            try
            {
                var providerQuery = isbn.HasValue ? isbn.Canonical! : query;
                var cacheKey = $"book-metadata:search:{NormalizeCacheKey(providerQuery)}";
                var external = await cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4);
                    entry.Size = 1;
                    return await provider.SearchAsync(providerQuery, cancellationToken);
                }) ?? [];
                var externalProviders = external.Select(x => x.Provider).Distinct().ToList();
                var externalIds = external
                    .SelectMany(x => new[] { x.ExternalId, x.WorkId })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var knownExternalBooks = await db.Books.AsNoTracking()
                    .Where(x => x.ExternalProvider != null && x.ExternalId != null
                        && (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId)
                        && externalProviders.Contains(x.ExternalProvider)
                        && externalIds.Contains(x.ExternalId))
                    .Select(x => new BookSearchDto(x.Id, x.ExternalProvider, x.ExternalId,
                        "local", x.Title, x.Subtitle, x.Author, x.CoverUrl,
                        x.Isbn10, x.Isbn13, x.Publisher, x.PublishedDate, x.Language,
                        null, null, null,
                        db.ReadingUnits.Any(unit => unit.BookId == x.Id)))
                    .ToListAsync(cancellationToken);
                var knownByKey = knownExternalBooks.ToDictionary(x =>
                    ExternalKey(x.ExternalProvider!, x.ExternalId!),
                    StringComparer.OrdinalIgnoreCase);
                var includedBookIds = local.Where(x => x.Id.HasValue)
                    .Select(x => x.Id!.Value).ToHashSet();
                foreach (var item in external)
                {
                    knownByKey.TryGetValue(
                        ExternalKey(item.Provider, item.ExternalId), out var known);
                    if (known is null && !string.IsNullOrWhiteSpace(item.WorkId))
                        knownByKey.TryGetValue(
                            ExternalKey(item.Provider, item.WorkId), out known);
                    if (known is not null)
                    {
                        if (known.Id is Guid knownId && includedBookIds.Add(knownId))
                            local.Add(known);
                        continue;
                    }
                    local.Add(new BookSearchDto(null, item.Provider, item.ExternalId, "external",
                        item.Title, item.Subtitle, item.Author, item.CoverUrl,
                        item.Isbn10, item.Isbn13, item.Publisher, item.PublishedDate,
                        item.Language, item.PageCount, item.Categories, item.Edition, false));
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

    private static string NormalizeCacheKey(string query) =>
        string.Join(' ', query.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
