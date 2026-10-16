using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public sealed class SourcePreparationService(SourcePreparationStore store, IBookNarrativeSourceProvider provider,
    MemoryPackGenerationTarget target, MemoryPackGenerationService generation, MnemoraDbContext db)
{
    public async Task<SourcePreparationStatus> CreateAsync(Guid owner, Guid bookId, CancellationToken token)
    {
        var book = await target.ReadAsync(bookId, cancellationToken: token);
        var identity = await IdentityAsync(bookId, token);
        var key = NarrativeSourceCatalog.Resolve(book) ?? throw new GenerationException("source_not_supported",
            NarrativeSourceCatalog.UnsupportedReason, 422);
        var step = store.Create(owner, bookId, book, key, BookSourceDiscovery.Fingerprint(identity));
        if (step.Lease is null) return step.Status;
        var lease = step.Lease;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lease.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var preferred = NarrativeSourceCatalog.GetLanguage(book).Language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
                ? "pt" : "en";
            var index = await provider.GetIndexAsync(key, cancellation.Token, preferred);
            index = index with
            {
                PreferredLanguage = preferred,
                Chapters = preferred == "pt" && index.Book.Language != "pt"
                    ? index.Chapters.Select(chapter => chapter with { Title = $"Capítulo {chapter.Order}" }).ToArray()
                    : index.Chapters
            };
            await CheckTargetAsync(owner, bookId, lease.PreparationId, cancellation.Token);
            return store.SetIndex(lease, index);
        }
        catch (Exception error)
        {
            var failure = Translate(error);
            store.Fail(lease, failure); throw failure;
        }
        finally { store.Release(lease); }
    }
    public SourcePreparationStatus? Current(Guid owner, Guid bookId) => store.Current(owner, bookId);
    public SourcePreparationStatus Get(Guid owner, Guid bookId, Guid id) => store.Snapshot(owner, bookId, id).Status;

    public async Task<SourcePreparationStatus> NextAsync(Guid owner, Guid bookId, Guid id,
        GenerationNextRequest request, CancellationToken token)
    {
        // Gate both fresh calls and replays; stale results cannot validate an edited edition.
        await CheckTargetAsync(owner, bookId, id, token);
        var step = store.BeginNext(owner, bookId, id, request);
        if (step.Lease is null) return step.Status;
        var lease = step.Lease;
        var snapshot = store.Snapshot(owner, bookId, id);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lease.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var index = snapshot.Index!;
            var chapter = index.Chapters[snapshot.Status.CompletedUnits];
            var source = await provider.GetChapterAsync(index, chapter, cancellation.Token);
            await CheckTargetAsync(owner, bookId, id, cancellation.Token);
            if (source is not null)
            {
                // The report and the assembler use identical paragraph normalization.
                var paragraphs = source.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
                var hashInput = System.Text.RegularExpressions.Regex.Split(paragraphs, @"\n[ \t]*\n+")
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim());
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n\n", hashInput))));
                source = source with { Provenance = source.Provenance with { UnitSlug = $"capitulo-{chapter.Order:000}", SourceHash = hash } };
            }
            return store.Complete(lease, source);
        }
        catch (Exception error)
        {
            var failure = Translate(error);
            store.Fail(lease, failure); throw failure;
        }
        finally { store.Release(lease); }
    }
    public async Task<GenerationSessionStatus> GenerateAsync(Guid owner, Guid bookId, Guid id,
        SourcePreparationGenerateRequest request, CancellationToken token)
    {
        if (request.OperationId == Guid.Empty || request.ExpectedRevision < 0)
            throw new GenerationException("invalid_operation", "Informe operationId e revisão válidos.");
        await CheckTargetAsync(owner, bookId, id, token);
        if (store.ExistingGeneration(owner, bookId, id) is { } existing)
            return await generation.GetStatusAsync(owner, bookId, existing, token);
        var lease = store.BeginGenerate(owner, bookId, id, request);
        GenerationSessionStatus? created = null;
        try
        {
            var snapshot = store.Snapshot(owner, bookId, id);
            var sources = snapshot.Index!.Chapters.Select((chapter, i) =>
                new GenerationSource(chapter.Title, snapshot.Sources[i]?.Text ?? "")).ToArray();
            var provenance = snapshot.Sources.Where(source => source is not null).Select(source => source!.Provenance).ToArray();
            var input = new GenerationStartRequest(sources, SourcePreparationStore.Attribution,
                snapshot.Index.SourceUrl, request.EditionConfirmed, request.IndexComplete);
            created = await generation.StartPreparedAsync(owner, bookId, input, provenance, snapshot.Status.Diagnostics, token);
            await CheckTargetAsync(owner, bookId, id, token);
            store.AttachGeneration(lease, created.RunId);
            return created;
        }
        catch
        {
            if (created is not null) generation.Cancel(owner, bookId, created.RunId);
            throw;
        }
        finally { store.Release(lease); }
    }
    public SourcePreparationStatus Cancel(Guid owner, Guid bookId, Guid id) => store.Cancel(owner, bookId, id);
    private async Task CheckTargetAsync(Guid owner, Guid bookId, Guid id, CancellationToken token)
    {
        var snapshot = store.Snapshot(owner, bookId, id);
        await target.ReadAsync(bookId, snapshot.ExpectedBook, token);
        var identity = await IdentityAsync(bookId, token);
        if (BookSourceDiscovery.Fingerprint(identity) != snapshot.IdentityFingerprint)
            throw new GenerationException("edition_changed", "A identidade bibliográfica mudou durante a coleta. Cancele e confira a edição antes de recomeçar.", 409);
    }
    private async Task<SourceBookIdentity> IdentityAsync(Guid bookId, CancellationToken token) =>
        await db.Books.AsNoTracking().Where(book => book.Id == bookId && book.CatalogKind != BookCatalogKind.Private)
            .Select(book => new SourceBookIdentity(book.Id, book.Title, book.Author, book.Isbn10, book.Isbn13,
                book.Language, book.Publisher, book.PublishedDate, book.ExternalProvider, book.ExternalId)).SingleOrDefaultAsync(token)
        ?? throw new GenerationException("book_not_found", "Livro não encontrado.", 404);
    private static GenerationException Translate(Exception error) => error switch
    {
        GenerationException known => known,
        OperationCanceledException => new("source_interrupted", "A leitura da fonte foi interrompida. O que já foi coletado foi preservado; consulte o progresso antes de retomar.", 503),
        _ => new("source_unavailable", "Não foi possível obter a fonte agora. O material anterior foi preservado, sem repetição automática.", 503)
    };
}
