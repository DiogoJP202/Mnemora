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
            var rows = await db.Books.AsNoTracking().OrderBy(x => x.Title)
                .Take(100).ToListAsync();
            return Results.Ok(rows.Select(CatalogDto.From));
        });

        books.MapGet("/search", async (
            string? q, MnemoraDbContext db, IBookMetadataProvider provider,
            ILogger<GoogleBooksProvider> logger, CancellationToken cancellationToken) =>
        {
            var query = q?.Trim();
            if (string.IsNullOrWhiteSpace(query)) return Results.Ok(Array.Empty<BookSearchDto>());
            if (query.Length > 100) return Results.BadRequest("Busca muito longa.");
            var term = query.ToLowerInvariant();
            var local = await db.Books.AsNoTracking().Where(x =>
                    x.Title.ToLower().Contains(term) || x.Author.ToLower().Contains(term))
                .OrderBy(x => x.Title).Take(20)
                .Select(x => new BookSearchDto(x.Id, null, "local",
                    x.Title, x.Author, x.CoverUrl)).ToListAsync(cancellationToken);
            if (!provider.IsConfigured) return Results.Ok(local);
            try
            {
                var external = await provider.SearchAsync(query, cancellationToken);
                var knownExternalIds = await db.Books.AsNoTracking()
                    .Where(x => x.ExternalProvider == "GoogleBooks" && x.ExternalId != null)
                    .Select(x => x.ExternalId!).ToListAsync(cancellationToken);
                local.AddRange(external.Where(x => !knownExternalIds.Contains(x.ExternalId))
                    .Select(x => new BookSearchDto(null, x.ExternalId, "external",
                        x.Title, x.Author, x.CoverUrl)));
                return Results.Ok(local);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Falha no provider de metadados de livros.");
                return local.Count > 0 ? Results.Ok(local)
                    : Results.Problem("Não foi possível buscar livros externos agora.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("catalog-external");

        books.MapGet("/{bookId:guid}", async (Guid bookId, MnemoraDbContext db) =>
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookId);
            return book is null ? Results.NotFound() : Results.Ok(CatalogDto.From(book));
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
            return Results.Ok(new
            {
                book = CatalogDto.From(book),
                knownEntities = await reader.KnownEntities(scope).CountAsync(),
                knownEvents = await reader.KnownEntities(scope)
                    .CountAsync(x => x.Type == LoreEntityType.Event)
            });
        }).RequireAuthorization();

        return app;
    }
}
