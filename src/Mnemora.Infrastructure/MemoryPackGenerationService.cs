using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class MemoryPackGenerationService(
    MemoryPackGenerationSessionStore store,
    MemoryPackGenerationTarget target,
    MemoryPackTransfer transfer,
    IMemoryPackGenerationProvider provider,
    IOptions<GenerationOptions> options,
    ILogger<MemoryPackGenerationService> logger)
{
    private readonly GenerationOptions configuration = options.Value;
    private readonly MemoryPackGenerationAssembler assembler = new(options.Value);

    public static bool ConfigurationAvailable(GenerationOptions options) =>
        GenerationConfiguration.Inspect(options).Count == 0;

    public async Task<GenerationSessionStatus> StartAsync(Guid ownerId, Guid bookId,
        GenerationStartRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var book = await target.ReadAsync(bookId, cancellationToken: cancellationToken);
        var issues = MemoryPackGenerationSourceParser.ValidateSources(request, configuration);
        if (issues.Any(issue => issue.IsBlocking))
            throw new GenerationException("invalid_sources", "Confira as fontes, atribuição e edição antes de gerar.");
        var document = assembler.Create(book, request);
        return store.Create(ownerId, bookId, request, document, issues.Select(issue => issue.Message).ToArray());
    }

    public Task<GenerationSessionStatus> GetStatusAsync(Guid ownerId, Guid bookId, Guid runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.GetStatus(ownerId, bookId, runId));
    }

    public async Task<GenerationSessionStatus> NextAsync(Guid ownerId, Guid bookId, Guid runId,
        GenerationNextRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var receipt = store.ResolveReceipt(ownerId, bookId, runId, request);
        if (receipt is not null) return receipt;
        var snapshot = store.Snapshot(ownerId, bookId, runId);
        if (request.ExpectedRevision != snapshot.Status.Revision)
            throw new GenerationException("revision_conflict",
                "A sessão mudou. Consulte o progresso antes de continuar.", 409);
        if (snapshot.CurrentSource is null)
            throw new GenerationException("generation_finished", "Todas as unidades já foram processadas.", 409);
        await CheckTargetAsync(ownerId, bookId, runId, snapshot.Document.Book, cancellationToken);
        var input = assembler.BuildInput(snapshot.Document, snapshot.CurrentSource, snapshot.Status.CompletedUnits);
        var missingSource = input.Paragraphs.Count == 0;
        var inputTokens = missingSource ? 0 : provider.EstimateInputTokens(input);
        var begun = store.Begin(ownerId, bookId, runId, request, !missingSource, inputTokens);
        if (begun.Receipt is not null) return begun.Receipt;
        var lease = begun.Lease!;
        var watch = Stopwatch.StartNew();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            lease.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        try
        {
            if (missingSource)
                return store.Complete(lease, snapshot.Document, snapshot.Report.Evidence, [], true);

            var result = await provider.ExtractAsync(input, cancellation.Token);
            store.RecordUsage(lease, result.InputTokens, result.OutputTokens);
            cancellation.Token.ThrowIfCancellationRequested();
            // The gate runs again after HTTP, without keeping a SQLite transaction open.
            await target.ReadAsync(bookId, snapshot.Document.Book, cancellation.Token);
            var assembled = assembler.Apply(snapshot.Document, input, result.Extraction, snapshot.Report.Evidence);
            var status = store.Complete(lease, assembled.Document, assembled.Evidence, assembled.Diagnostics, false);
            logger.LogInformation("Generation {RunId} unit {UnitSlug} completed using {Model} prompt {PromptVersion} in {DurationMs} ms; tokens {Tokens}",
                runId, input.UnitSlug, configuration.Model, GenerationPrompt.Version, watch.ElapsedMilliseconds,
                (long)result.InputTokens + result.OutputTokens);
            return status;
        }
        catch (OperationCanceledException)
        {
            var cancelled = lease.CancellationToken.IsCancellationRequested;
            var error = new GenerationException(cancelled ? "generation_cancelled" : "generation_interrupted",
                cancelled ? "A sessão foi cancelada; o último resultado concluído foi preservado."
                    : "A chamada foi interrompida. O último resultado concluído foi preservado; a retomada é manual.",
                cancelled ? 409 : 503);
            store.Fail(lease, error);
            throw error;
        }
        catch (GenerationException error)
        {
            store.Fail(lease, error);
            logger.LogWarning("Generation {RunId} unit {UnitSlug} paused: {Category}; elapsed {DurationMs} ms",
                runId, input.UnitSlug, error.Code, watch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception error)
        {
            // Do not log exception messages that could include supplied/model text.
            logger.LogError("Generation {RunId} unit {UnitSlug} failed with {ExceptionType}",
                runId, input.UnitSlug, error.GetType().Name);
            var failure = new GenerationException("generation_failed",
                "Não foi possível concluir esta unidade. O resultado anterior foi preservado.", 503);
            store.Fail(lease, failure);
            throw failure;
        }
        finally { store.Release(lease); }
    }

    public async Task<GenerationSessionResult> ResultAsync(Guid ownerId, Guid bookId, Guid runId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = store.Snapshot(ownerId, bookId, runId);
        await CheckTargetAsync(ownerId, bookId, runId, snapshot.Document.Book, cancellationToken);
        var validation = await transfer.ValidateAsync(bookId, snapshot.Document, cancellationToken);
        if (validation is null)
            throw new GenerationException("book_not_found", "Livro não encontrado.", 404);
        // Preserve the exact canonical bytes hashed in the report. Browser reformatting
        // would otherwise change the hash even without changing the document's values.
        var documentJson = JsonSerializer.Serialize(snapshot.Document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new(snapshot.Status, snapshot.Document, snapshot.Report, validation.Validation, documentJson);
    }

    public GenerationSessionStatus Cancel(Guid ownerId, Guid bookId, Guid runId) => store.Cancel(ownerId, bookId, runId);

    private async Task CheckTargetAsync(Guid ownerId, Guid bookId, Guid runId, MemoryPackBook expected,
        CancellationToken cancellationToken)
    {
        try { await target.ReadAsync(bookId, expected, cancellationToken); }
        catch (GenerationException error)
        {
            try { store.Cancel(ownerId, bookId, runId, error.Message); }
            catch (GenerationException missing) when (missing.StatusCode == 404) { }
            throw;
        }
    }

    private void EnsureAvailable()
    {
        if (!ConfigurationAvailable(configuration))
            throw new GenerationException("generation_disabled", "A geração assistida não está configurada neste servidor.", 503);
    }
}
