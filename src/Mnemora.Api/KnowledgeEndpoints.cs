using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var lore = app.MapGroup("/api").RequireAuthorization().WithTags("Lore");

        lore.MapGet("/books/{bookId:guid}/entities", async (
            Guid bookId, string? type, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            if (scope is null) return Results.NotFound();
            LoreEntityType? entityType = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!type.All(char.IsLetter)
                    || !Enum.TryParse<LoreEntityType>(type, true, out var parsed)
                    || !Enum.IsDefined(parsed))
                    return Results.BadRequest();
                entityType = parsed;
            }
            return Results.Ok(await reader.EntitiesAsync(scope, entityType));
        });

        lore.MapGet("/books/{bookId:guid}/entities/search", async (
            Guid bookId, string? q, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.EntitiesAsync(scope, query: q));
        });

        lore.MapGet("/books/{bookId:guid}/recall", async (
            Guid bookId, string? q, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.EntitiesAsync(scope, query: q));
        });

        lore.MapGet("/books/{bookId:guid}/timeline", async (
            Guid bookId, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.TimelineAsync(scope));
        });

        lore.MapGet("/books/{bookId:guid}/factions", async (
            Guid bookId, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.EntitiesAsync(scope, LoreEntityType.Faction));
        });

        lore.MapGet("/books/{bookId:guid}/locations", async (
            Guid bookId, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(UserId(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.EntitiesAsync(scope, LoreEntityType.Location));
        });

        lore.MapGet("/entities/{entityId:guid}", async (
            Guid entityId, ClaimsPrincipal user, KnowledgeReader reader,
            MnemoraDbContext db) =>
        {
            var bookId = await db.LoreEntities.AsNoTracking()
                .Where(x => x.Id == entityId).Select(x => (Guid?)x.BookId).FirstOrDefaultAsync();
            if (bookId is null) return Results.NotFound();
            var scope = await reader.ScopeAsync(UserId(user), bookId.Value);
            if (scope is null) return Results.NotFound();
            var detail = await reader.EntityAsync(scope, entityId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        return app;
    }

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
