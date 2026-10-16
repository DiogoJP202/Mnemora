using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminSourcePreparationEndpoints
{
    public static IEndpointRouteBuilder MapAdminSourcePreparationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/books/{bookId:guid}/source-preparations")
            .RequireAuthorization("Admin").WithTags("Admin Source Preparation");
        group.MapPost("", async (Guid bookId, HttpContext context, SourcePreparationService service, CancellationToken token) =>
            await AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, async () =>
            {
                await EmptyAsync(context.Request, token);
                var status = await service.CreateAsync(CurrentUser.Id(context.User), bookId, token);
                return Results.Created($"/api/admin/books/{bookId}/source-preparations/{status.PreparationId}", status);
            })).RequireRateLimiting("import-external");
        group.MapGet("/current", (Guid bookId, HttpContext context, SourcePreparationService service) =>
            AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, () => Task.FromResult<IResult>(
                service.Current(CurrentUser.Id(context.User), bookId) is { } current ? Results.Ok(current) : Results.NotFound())));
        group.MapGet("/{preparationId:guid}", (Guid bookId, Guid preparationId, HttpContext context, SourcePreparationService service) =>
            AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, () => Task.FromResult<IResult>(
                Results.Ok(service.Get(CurrentUser.Id(context.User), bookId, preparationId)))));
        group.MapPost("/{preparationId:guid}/next", async (Guid bookId, Guid preparationId, HttpContext context,
            SourcePreparationService service, CancellationToken token) =>
            await AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, async () =>
                Results.Ok(await service.NextAsync(CurrentUser.Id(context.User), bookId, preparationId,
                    await AdminMemoryPackGenerationEndpoints.ReadAsync<GenerationNextRequest>(context.Request, token), token))))
            .RequireRateLimiting("memory-write");
        group.MapPost("/{preparationId:guid}/generate", async (Guid bookId, Guid preparationId, HttpContext context,
            SourcePreparationService service, CancellationToken token) =>
            await AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, async () =>
                Results.Ok(await service.GenerateAsync(CurrentUser.Id(context.User), bookId, preparationId,
                    await AdminMemoryPackGenerationEndpoints.ReadAsync<SourcePreparationGenerateRequest>(context.Request, token), token))))
            .RequireRateLimiting("memory-write");
        group.MapPost("/{preparationId:guid}/cancel", async (Guid bookId, Guid preparationId, HttpContext context,
            SourcePreparationService service, CancellationToken token) =>
            await AdminMemoryPackGenerationEndpoints.ExecuteAsync(context, async () =>
            {
                await EmptyAsync(context.Request, token);
                return Results.Ok(service.Cancel(CurrentUser.Id(context.User), bookId, preparationId));
            })).RequireRateLimiting("memory-write");
        return app;
    }
    private static async Task EmptyAsync(HttpRequest request, CancellationToken token)
    {
        if (request.HasJsonContentType())
        { await AdminMemoryPackGenerationEndpoints.ReadAsync<EmptyRequest>(request, token); return; }
        if (await request.Body.ReadAsync(new byte[1], token) != 0)
            throw new GenerationException("invalid_body", "Esta operação não aceita fontes, URLs ou instruções do navegador.");
    }
    private sealed record EmptyRequest;
}
