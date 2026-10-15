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
            var catalog = await db.Books.AsNoTracking()
                .Where(x => x.CatalogKind == BookCatalogKind.Curated)
                .OrderBy(x => x.Title).Take(100)
                .ToListAsync();
            var availability = await AvailabilityAsync(db, catalog, null);
            return Results.Ok(catalog.Select(book => ToBookDto(
                book, availability[book.Id])));
        });

        books.MapGet("/search", (string? q, ClaimsPrincipal user, MnemoraDbContext db,
            IBookMetadataProvider provider, IMemoryCache cache, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
            SearchAsync(q, TryUserId(user), db, provider, cache, loggerFactory, cancellationToken))
            .RequireRateLimiting("catalog-external");

        books.MapGet("/{bookId:guid}", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = TryUserId(user);
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == bookId
                && (x.CatalogKind != BookCatalogKind.Private || x.OwnerUserId == userId));
            if (book is null) return Results.NotFound();
            var availability = (await AvailabilityAsync(db, [book], userId))[book.Id];
            return Results.Ok(ToBookDto(book, availability));
        });

        books.MapPost("/{bookId:guid}/memory-pack-request", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == bookId && x.CatalogKind != BookCatalogKind.Private);
            if (book is null || !await db.UserBooks.AsNoTracking().AnyAsync(x =>
                    x.BookId == bookId && x.UserId == userId))
                return Results.NotFound();

            var hasUnits = await db.ReadingUnits.AsNoTracking()
                .AnyAsync(x => x.BookId == bookId);
            var hasLore = await db.LoreEntities.AsNoTracking()
                .AnyAsync(x => x.BookId == bookId);
            if (MemoryPackAvailabilityPolicy.Evaluate(book, hasUnits, hasLore)
                .MemoryPackAvailable)
                return Results.Conflict("O pacote de memória deste livro já está disponível.");

            var existing = await db.MemoryPackRequests.FirstOrDefaultAsync(x =>
                x.BookId == bookId && x.UserId == userId);
            if (existing is null)
            {
                existing = new MemoryPackRequest { BookId = bookId, UserId = userId };
                db.MemoryPackRequests.Add(existing);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    db.Entry(existing).State = EntityState.Detached;
                    existing = await db.MemoryPackRequests.AsNoTracking().SingleAsync(x =>
                        x.BookId == bookId && x.UserId == userId);
                }
            }
            return Results.Ok(new { requested = true, existing.CreatedAt });
        }).RequireAuthorization().RequireRateLimiting("memory-write");

        books.MapGet("/{bookId:guid}/reading-units", async (
            Guid bookId, ClaimsPrincipal user, MnemoraDbContext db) =>
        {
            var userId = CurrentUser.Id(user);
            var access = await (from library in db.UserBooks.AsNoTracking()
                join book in db.Books.AsNoTracking() on library.BookId equals book.Id
                where library.UserId == userId && library.BookId == bookId
                    && (book.CatalogKind != BookCatalogKind.Private
                        || book.OwnerUserId == userId)
                    && book.ReadingOutlineStatus == EditorialContentStatus.Published
                select new { Library = library, Book = book }).FirstOrDefaultAsync();
            if (access is null) return Results.NotFound();
            var units = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == bookId)
                .OrderBy(x => x.OrderIndex).ToListAsync();
            if (units.Count == 0) return Results.NotFound();
            var currentOrder = access.Library.CurrentReadingUnitId is Guid currentId
                ? units.FirstOrDefault(x => x.Id == currentId)?.OrderIndex
                : null;
            return Results.Ok(units.Select(x =>
            {
                var known = KnowledgeVisibility.CanSee(currentOrder, x.OrderIndex);
                return new ReadingUnitDto(x.Id, x.ParentUnitId, x.SafeLabel,
                    known ? x.Title : x.SafeLabel, x.Type.ToString(), x.OrderIndex,
                    known, access.Library.CurrentReadingUnitId == x.Id);
            }));
        }).RequireAuthorization();

        books.MapGet("/{bookId:guid}/overview", async (
            Guid bookId, System.Security.Claims.ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            var book = await db.Books.AsNoTracking().FirstAsync(x => x.Id == bookId);
            var availability = (await AvailabilityAsync(
                db, [book], scope.UserId))[book.Id];
            return Results.Ok(new
            {
                book = ToBookDto(book, availability),
                knownEntities = await reader.KnownEntities(scope).CountAsync(),
                knownEvents = await reader.KnownEntities(scope)
                    .CountAsync(x => x.Type == LoreEntityType.Event)
            });
        }).RequireAuthorization();

        return app;
    }

    internal static async Task<IResult> SearchAsync(string? q, Guid? userId, MnemoraDbContext db,
        IBookMetadataProvider provider, IMemoryCache cache, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var query = q?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return Results.Ok(Array.Empty<BookSearchDto>());
        if (query.Length > 100) return Results.BadRequest("Busca muito longa.");
        var term = query.ToLowerInvariant();
        var isbn = BookIsbn.Normalize(query);
        var localBooks = await db.Books.AsNoTracking().Where(x =>
                (x.CatalogKind != BookCatalogKind.Private || (userId != null && x.OwnerUserId == userId))
                && (x.Title.ToLower().Contains(term)
                    || (x.Subtitle != null && x.Subtitle.ToLower().Contains(term))
                    || x.Author.ToLower().Contains(term)
                    || (x.Isbn10 != null && x.Isbn10.Contains(term))
                    || (x.Isbn13 != null && x.Isbn13.Contains(term))
                    || (isbn.Isbn10 != null && x.Isbn10 == isbn.Isbn10)
                    || (isbn.Isbn13 != null && x.Isbn13 == isbn.Isbn13)))
            .OrderBy(x => x.Title).Take(20)
            .ToListAsync(cancellationToken);
        var localAvailability = await AvailabilityAsync(db, localBooks, userId);
        var local = localBooks.Select(book => ToSearchDto(
            book, localAvailability[book.Id])).ToList();
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
            var knownExternalBookRows = await db.Books.AsNoTracking()
                .Where(x => x.ExternalProvider != null && x.ExternalId != null
                    && (x.CatalogKind != BookCatalogKind.Private || (userId != null && x.OwnerUserId == userId))
                    && externalProviders.Contains(x.ExternalProvider)
                    && externalIds.Contains(x.ExternalId))
                .ToListAsync(cancellationToken);
            var knownAvailability = await AvailabilityAsync(
                db, knownExternalBookRows, userId);
            var knownExternalBooks = knownExternalBookRows.Select(book =>
                ToSearchDto(book, knownAvailability[book.Id])).ToList();
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
                    item.Language, item.PageCount, item.Categories, item.Edition, false,
                    false, "Unavailable", false));
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
    }

    private static Guid? TryUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId : null;

    private static async Task<Dictionary<Guid, MemoryPackAvailability>> AvailabilityAsync(
        MnemoraDbContext db, IReadOnlyCollection<Book> books, Guid? userId)
    {
        var ids = books.Select(x => x.Id).ToArray();
        var unitBookIds = await db.ReadingUnits.AsNoTracking()
            .Where(x => ids.Contains(x.BookId)).Select(x => x.BookId).Distinct()
            .ToListAsync();
        var loreBookIds = await db.LoreEntities.AsNoTracking()
            .Where(x => ids.Contains(x.BookId)).Select(x => x.BookId).Distinct()
            .ToListAsync();
        var requestedBookIds = userId is Guid readerId
            ? await db.MemoryPackRequests.AsNoTracking()
                .Where(x => x.UserId == readerId && ids.Contains(x.BookId))
                .Select(x => x.BookId).ToListAsync()
            : [];
        var withUnits = unitBookIds.ToHashSet();
        var withLore = loreBookIds.ToHashSet();
        var requested = requestedBookIds.ToHashSet();
        return books.ToDictionary(book => book.Id, book =>
            MemoryPackAvailabilityPolicy.Evaluate(
                book, withUnits.Contains(book.Id), withLore.Contains(book.Id),
                requested.Contains(book.Id)));
    }

    private static BookDto ToBookDto(Book book, MemoryPackAvailability availability) =>
        new(book.Id, book.Title, book.Subtitle, book.Author,
            book.Description, book.CoverUrl, book.Isbn10, book.Isbn13,
            book.Publisher, book.PublishedDate, book.Language,
            availability.MemoryPackAvailable,
            availability.ReadingOutlineAvailable,
            availability.Status,
            availability.CanRequest);

    private static BookSearchDto ToSearchDto(
        Book book, MemoryPackAvailability availability) =>
        new(book.Id, book.ExternalProvider, book.ExternalId, "local",
            book.Title, book.Subtitle, book.Author, book.CoverUrl,
            book.Isbn10, book.Isbn13, book.Publisher, book.PublishedDate,
            book.Language, null, null, null,
            availability.MemoryPackAvailable,
            availability.ReadingOutlineAvailable,
            availability.Status,
            availability.CanRequest);

    private static string ExternalKey(string provider, string externalId) =>
        $"{provider}\u001f{externalId}";

    private static string NormalizeCacheKey(string query) =>
        string.Join(' ', query.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
