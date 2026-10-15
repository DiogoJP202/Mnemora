using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminSourceDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapAdminSourceDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/books/{bookId:guid}/source-discovery", async (
            Guid bookId, MnemoraDbContext db, BookSourceDiscovery discovery,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var identity = await IdentityAsync(db, bookId, cancellationToken);
            if (identity is null) return Results.NotFound();
            try
            {
                var result = await discovery.DiscoverAsync(identity, cancellationToken);
                var current = await IdentityAsync(db, bookId, cancellationToken);
                if (current is null) return Results.NotFound();
                if (BookSourceDiscovery.Fingerprint(current) != BookSourceDiscovery.Fingerprint(identity))
                    return Results.Conflict("A identidade bibliográfica mudou durante a consulta. Atualize a página e consulte novamente.");
                return Results.Ok(result);
            }
            catch (SourceDiscoveryBusyException)
            {
                context.Response.Headers.RetryAfter = "15";
                return Results.Problem("Outra descoberta de fontes está em andamento. Aguarde e tente novamente.", statusCode: 429);
            }
        }).RequireAuthorization("Admin").RequireRateLimiting("import-external")
            .WithTags("Admin Sources");
        return app;
    }

    private static Task<SourceBookIdentity?> IdentityAsync(MnemoraDbContext db, Guid id, CancellationToken token) =>
        db.Books.AsNoTracking().Where(book => book.Id == id && book.CatalogKind != BookCatalogKind.Private)
            .Select(book => new SourceBookIdentity(book.Id, book.Title, book.Author,
                book.Isbn10, book.Isbn13, book.Language, book.Publisher, book.PublishedDate,
                book.ExternalProvider, book.ExternalId)).SingleOrDefaultAsync(token);
}
