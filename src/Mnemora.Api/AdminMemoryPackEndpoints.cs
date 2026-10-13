using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminMemoryPackEndpoints
{
    public const int MaxDocumentBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        MaxDepth = 64
    };

    public static IEndpointRouteBuilder MapAdminMemoryPackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/books/{bookId:guid}/memory-pack")
            .RequireAuthorization("Admin").WithTags("Admin Memory Packs");

        group.MapGet("/export", async (Guid bookId, MemoryPackTransfer transfer,
            CancellationToken cancellationToken) =>
        {
            var document = await transfer.ExportAsync(bookId, cancellationToken);
            return document is null ? Results.NotFound() : Results.Json(document, DocumentJson);
        });

        group.MapPost("/validate", async (Guid bookId, HttpRequest request,
            MnemoraDbContext db, MemoryPackTransfer transfer, CancellationToken cancellationToken) =>
        {
            if (!await IsSharedBookAsync(db, bookId, cancellationToken)) return Results.NotFound();
            var body = await ReadDocumentAsync(request, cancellationToken);
            if (body.Error is not null) return body.Error;
            var outcome = await transfer.ValidateAsync(bookId, body.Document!, cancellationToken);
            return outcome is null ? Results.NotFound() : Results.Ok(outcome.Validation);
        });

        group.MapPost("/import", async (Guid bookId, HttpRequest request,
            MnemoraDbContext db, MemoryPackTransfer transfer, CancellationToken cancellationToken) =>
        {
            if (!await IsSharedBookAsync(db, bookId, cancellationToken)) return Results.NotFound();
            var body = await ReadDocumentAsync(request, cancellationToken);
            if (body.Error is not null) return body.Error;
            var outcome = await transfer.ImportAsync(bookId, body.Document!, cancellationToken);
            if (outcome is null) return Results.NotFound();
            if (!outcome.Validation.IsValid)
                return Results.Json(outcome.Validation, statusCode: outcome.Conflict
                    ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
            var result = new MemoryPackImportResult(outcome.Validation.AlreadyImported,
                outcome.Validation.Counts);
            return outcome.Validation.AlreadyImported ? Results.Ok(result)
                : Results.Created($"/api/admin/books/{bookId}/memory-pack/export", result);
        });
        return app;
    }

    private static Task<bool> IsSharedBookAsync(MnemoraDbContext db, Guid bookId,
        CancellationToken cancellationToken) => db.Books.AsNoTracking().AnyAsync(x =>
            x.Id == bookId && x.CatalogKind != BookCatalogKind.Private, cancellationToken);

    private static async Task<DocumentRead> ReadDocumentAsync(HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
            return new(null, Results.Problem("Envie o pacote como application/json.",
                statusCode: StatusCodes.Status415UnsupportedMediaType));
        if (request.ContentLength > MaxDocumentBytes) return TooLarge();
        using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await request.Body.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (content.Length + count > MaxDocumentBytes) return TooLarge();
            await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        try
        {
            using var json = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            EnsureUniqueProperties(json.RootElement, "$");
            var document = json.RootElement.Deserialize<MemoryPackDocument>(DocumentJson);
            if (document is null)
                return InvalidJson("$", "Informe um objeto JSON com o pacote de memória.");
            return new(document, null);
        }
        catch (JsonException exception)
        {
            return InvalidJson(exception.Path ?? "$",
                "JSON inválido, tipo de campo incorreto ou campo não reconhecido no formato v1.");
        }
    }

    private static void EnsureUniqueProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Campo duplicado.", $"{path}.{property.Name}", null, null);
                EnsureUniqueProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                EnsureUniqueProperties(item, $"{path}[{index++}]");
        }
    }

    private static DocumentRead InvalidJson(string path, string message) => new(null,
        Results.Json(new MemoryPackValidationResult(false, false,
            new(0, 0, 0, 0, 0, 0), [new(path, message)]),
            statusCode: StatusCodes.Status400BadRequest));
    private static DocumentRead TooLarge() => new(null, Results.Problem(
        "O arquivo precisa ter no máximo 2 MiB.", statusCode: StatusCodes.Status413PayloadTooLarge));
    private sealed record DocumentRead(MemoryPackDocument? Document, IResult? Error);
}
