using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminMemoryPackGenerationEndpoints
{
    private const int MaxRequestBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    public static IEndpointRouteBuilder MapAdminMemoryPackGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/books/{bookId:guid}/memory-pack/generations")
            .RequireAuthorization("Admin").WithTags("Admin Memory Pack Generation");

        group.MapGet("/availability", async (Guid bookId, HttpContext context,
            MemoryPackGenerationTarget target, MemoryPackGenerationSessionStore store,
            IOptions<GenerationOptions> configured, IHostEnvironment environment, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
            {
                string? reason = null;
                try { await target.ReadAsync(bookId, cancellationToken: cancellationToken); }
                catch (GenerationException exception) when (exception.StatusCode == 409) { reason = exception.Message; }
                var options = configured.Value;
                var issues = GenerationConfiguration.Inspect(options);
                var enabled = issues.Count == 0;
                if (!enabled && reason is null)
                    reason = "Configure a geração assistida no backend para iniciar. A importação manual continua disponível.";
                return Results.Ok(new
                {
                    enabled = enabled && reason is null, reason, model = "openai/gpt-oss-120b", options.MaxUnits,
                    options.MaxSourceBytes, options.MinCallIntervalSeconds,
                    configuration = new { ready = enabled, issues, localSetup = environment.IsDevelopment() },
                    existingSession = store.FindExisting(CurrentUser.Id(context.User))
                });
            }));

        group.MapPost("/parse", async (Guid bookId, HttpContext context,
            MemoryPackGenerationTarget target, IOptions<GenerationOptions> options,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
            {
                await target.ReadAsync(bookId, cancellationToken: cancellationToken);
                var input = await ReadAsync<ParseRequest>(context.Request, cancellationToken);
                return Results.Ok(MemoryPackGenerationSourceParser.Parse(input.Text,
                    options.Value.MaxUnits, options.Value.MaxSourceBytes));
            })).RequireRateLimiting("memory-write");

        group.MapPost("", async (Guid bookId, HttpContext context,
            MemoryPackGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
            {
                var input = await ReadAsync<GenerationStartRequest>(context.Request, cancellationToken);
                var session = await service.StartAsync(CurrentUser.Id(context.User), bookId, input, cancellationToken);
                return Results.Created($"/api/admin/books/{bookId}/memory-pack/generations/{session.RunId}", session);
            })).RequireRateLimiting("memory-write");

        group.MapGet("/{runId:guid}", async (Guid bookId, Guid runId, HttpContext context,
            MemoryPackGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.GetStatusAsync(
                CurrentUser.Id(context.User), bookId, runId, cancellationToken))));

        group.MapPost("/{runId:guid}/next", async (Guid bookId, Guid runId, HttpContext context,
            MemoryPackGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
            {
                var input = await ReadAsync<GenerationNextRequest>(context.Request, cancellationToken);
                return Results.Ok(await service.NextAsync(CurrentUser.Id(context.User), bookId, runId,
                    input, cancellationToken));
            })).RequireRateLimiting("memory-write");

        group.MapGet("/{runId:guid}/result", async (Guid bookId, Guid runId, HttpContext context,
            MemoryPackGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.ResultAsync(
                CurrentUser.Id(context.User), bookId, runId, cancellationToken))));

        group.MapPost("/{runId:guid}/cancel", async (Guid bookId, Guid runId, HttpContext context,
            MemoryPackGenerationService service) => await ExecuteAsync(context, () =>
                Task.FromResult<IResult>(Results.Ok(service.Cancel(CurrentUser.Id(context.User), bookId, runId)))))
            .RequireRateLimiting("memory-write");
        return app;
    }

    internal static async Task<IResult> ExecuteAsync(HttpContext context, Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (GenerationException exception)
        {
            if (exception.RetryAfterSeconds is { } retry)
                context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(title: "Não foi possível concluir a geração assistida.",
                detail: exception.Message, statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = exception.Code, ["retryAfterSeconds"] = exception.RetryAfterSeconds
                });
        }
    }

    internal static async Task<T> ReadAsync<T>(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
            throw new GenerationException("invalid_content_type", "Envie os dados como application/json.", 415);
        if (request.ContentLength > MaxRequestBytes) throw TooLarge();
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await request.Body.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (bytes.Length + count > MaxRequestBytes) throw TooLarge();
            bytes.Write(buffer, 0, count);
        }
        try
        {
            using var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)),
                new JsonDocumentOptions { MaxDepth = 64 });
            UniqueProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new GenerationException("invalid_json",
                "JSON inválido: confira os campos obrigatórios, os tipos e a ausência de campos extras ou duplicados.");
        }
    }

    private static void UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                UniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) UniqueProperties(item);
    }

    private static GenerationException TooLarge() => new("request_too_large",
        "A requisição precisa ter no máximo 2 MiB.", 413);
    private sealed record ParseRequest(string Text);
}
