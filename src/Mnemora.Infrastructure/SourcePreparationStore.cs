using System.Text;
using System.Text.Json;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed record SourcePreparationSnapshot(SourcePreparationStatus Status, MemoryPackBook ExpectedBook,
    string BookKey, NarrativeSourceIndex? Index, IReadOnlyList<CollectedNarrativeSource?> Sources, string IdentityFingerprint);
public sealed record SourcePreparationLease(Guid PreparationId, Guid? OperationId, int ExpectedRevision,
    CancellationToken CancellationToken);
public sealed record SourcePreparationStep(SourcePreparationStatus Status, SourcePreparationLease? Lease);

// A separate, bounded pre-generation workspace; it never writes editorial tables.
public sealed class SourcePreparationStore(TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object sync = new();
    private readonly Dictionary<Guid, Session> sessions = [];
    private Guid? active;
    private DateTimeOffset? cooldown;
    private bool stopping;
    private const int MaxBytes = 4 * 1024 * 1024;
    private const int MaxSourceBytes = 512 * 1024;
    private sealed record Receipt(int Revision, byte[]? Status, GenerationException? Error);
    private sealed class Session
    {
        public required Guid Id, Owner, BookId;
        public required MemoryPackBook Book;
        public required string BookKey;
        public required string IdentityFingerprint;
        public required DateTimeOffset Created, Touched;
        public NarrativeSourceIndex? Index;
        public byte[]?[] Sources = [];
        public string[] UnitStates = [];
        public int[] Attempts = [];
        public IReadOnlyList<SourcePreparationUnit>? TransferredUnits;
        public string State = "Collecting";
        public int Revision, Cursor;
        public Guid? GenerationRunId;
        public Guid? GenerateOperation;
        public int GenerateRevision;
        public bool Expired;
        public SourcePreparationLease? Active;
        public CancellationTokenSource Cancellation = new();
        public Dictionary<Guid, Receipt> Receipts = [];
        public List<string> Diagnostics = [];
    }

    public SourcePreparationStep Create(Guid owner, Guid bookId, MemoryPackBook book, string key, string fingerprint) => Access<SourcePreparationStep>(() =>
    {
        if (stopping) throw Error("preparation_stopping", "O servidor está encerrando. Preserve o material coletado.", 503);
        var previous = sessions.Values.FirstOrDefault(s => s.Owner == owner);
        if (previous is not null && (previous.Active is not null || previous.State != "Cancelled"
            && (previous.GenerationRunId is null || previous.BookId == bookId)))
        {
            if (previous.BookId != bookId) throw Error("preparation_exists", "Há uma coleta em outro livro. Retome ou cancele essa coleta antes de iniciar outra.", 409);
            if (previous.IdentityFingerprint != fingerprint) throw Error("edition_changed", "A edição foi alterada. Cancele a coleta anterior e confira a edição antes de recomeçar.", 409);
            return new(Status(previous), null);
        }
        CheckCooldown();
        if (active is not null) throw Busy();
        if (sessions.Count - (previous is null ? 0 : 1) >= 2)
            throw Error("preparation_capacity", "As duas vagas de coleta estão ocupadas. Aguarde ou cancele uma coleta.", 503);
        if (previous is not null) { sessions.Remove(previous.Id); previous.Cancellation.Dispose(); }
        var now = clock.GetUtcNow();
        var session = new Session { Id = Guid.NewGuid(), Owner = owner, BookId = bookId, Book = book,
            BookKey = key, IdentityFingerprint = fingerprint, Created = now, Touched = now };
        sessions.Add(session.Id, session);
        var lease = Activate(session, null, 0);
        return new(Status(session), lease);
    });

    public SourcePreparationStatus? Current(Guid owner, Guid bookId) => Access(() =>
    {
        var s = sessions.Values.FirstOrDefault(s => s.Owner == owner && s.BookId == bookId && !s.Expired);
        if (s is null) return null;
        s.Touched = clock.GetUtcNow();
        return Status(s);
    });
    public SourcePreparationSnapshot Snapshot(Guid owner, Guid bookId, Guid id) => Access(() =>
    {
        var s = Find(owner, bookId, id); s.Touched = clock.GetUtcNow();
        return new SourcePreparationSnapshot(Status(s), s.Book, s.BookKey, s.Index,
            s.Sources.Select(bytes => bytes is null ? null : Deserialize<CollectedNarrativeSource>(bytes)).ToArray(), s.IdentityFingerprint);
    });
    public SourcePreparationStatus SetIndex(SourcePreparationLease lease, NarrativeSourceIndex index) => Access(() =>
    {
        var s = ForLease(lease);
        if (index.Chapters.Count is < 1 or > 64 || index.Chapters.Where((c, i) =>
                c.Order != i + 1 || string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 300
                || string.IsNullOrWhiteSpace(c.PageTitle) || c.PageTitle.Length > 500).Any()
            || index.Chapters.Select(c => c.PageTitle).Distinct(StringComparer.Ordinal).Count() != index.Chapters.Count
            || Serialize(index).Length > 128 * 1024)
            throw Error("invalid_source_index", "O índice da fonte não possui capítulos consecutivos e verificáveis.", 422);
        s.Index = index;
        s.Sources = new byte[]?[index.Chapters.Count]; s.UnitStates = Enumerable.Repeat("Pending", index.Chapters.Count).ToArray();
        s.Attempts = new int[index.Chapters.Count];
        AddDiagnostics(s, index.Diagnostics.Concat(["Fonte da obra, não confirmação da edição: confira tradução, capítulos e se a edição é integral. A revisão e a publicação continuam manuais."]));
        s.Revision++;
        return Status(s);
    });
    public SourcePreparationStep BeginNext(Guid owner, Guid bookId, Guid id, GenerationNextRequest request) => Access<SourcePreparationStep>(() =>
    {
        var s = Find(owner, bookId, id);
        ValidateOperation(request.OperationId, request.ExpectedRevision);
        if (s.Receipts.TryGetValue(request.OperationId, out var receipt))
        {
            if (receipt.Revision != request.ExpectedRevision) throw Conflict();
            if (receipt.Error is not null) throw receipt.Error;
            return new(Deserialize<SourcePreparationStatus>(receipt.Status!), null);
        }
        if (s.Active?.OperationId == request.OperationId && s.Active.ExpectedRevision == request.ExpectedRevision)
            return new(Status(s), null);
        if (s.Active is not null || s.Revision != request.ExpectedRevision) throw Conflict();
        if (s.State == "Cancelled" || s.GenerationRunId is not null || s.Index is null)
            throw Error("preparation_unavailable", "A coleta não pode continuar. Inicie outra preparação.", 409);
        if (s.Cursor == s.Sources.Length) return new(Status(s), null);
        if (s.Receipts.Count >= 130) throw Error("preparation_operations_limit", "O limite de operações desta coleta foi atingido.", 422);
        CheckCooldown();
        if (active is not null) throw Busy();
        s.Attempts[s.Cursor]++;
        return new(Status(s), Activate(s, request.OperationId, request.ExpectedRevision));
    });
    public SourcePreparationStatus Complete(SourcePreparationLease lease, CollectedNarrativeSource? source) => Access(() =>
    {
        var s = ForLease(lease);
        var chapter = s.Index!.Chapters[s.Cursor];
        var bytes = source is null ? null : Serialize(source);
        if (source is not null && (source.Order != chapter.Order || source.Title != chapter.Title
            || string.IsNullOrWhiteSpace(source.Text)))
            throw Error("source_unit_mismatch", "A fonte obtida não corresponde ao capítulo definido pelo servidor.", 422);
        if (bytes is not null && s.Sources.Sum(b => (long)(b?.Length ?? 0)) + bytes.Length > MaxSourceBytes)
            throw Error("sources_too_large", "As fontes excedem 512 KiB. Nenhum trecho foi cortado; preserve o resultado parcial.", 422);
        CheckRetained(s, (bytes?.Length ?? 0) + 128 * 1024);
        s.Sources[s.Cursor] = bytes;
        s.UnitStates[s.Cursor] = source is null ? "MissingSource" : "Collected";
        if (source is null) AddDiagnostics(s, [$"Capítulo {chapter.Order}: não foi encontrada uma sinopse utilizável. A lacuna foi preservada."]);
        else if (s.Index.PreferredLanguage == "pt" && source.Provenance.Language != "pt")
            AddDiagnostics(s, ["Algumas sinopses só estavam disponíveis em inglês. O conteúdo gerado será solicitado no idioma de destino; confira os nomes próprios e as evidências dessas unidades no relatório."]);
        s.Cursor++; s.Revision++; s.Touched = clock.GetUtcNow();
        if (s.Cursor == s.Sources.Length) s.State = s.UnitStates.All(state => state == "Collected") ? "Ready" : "Partial";
        var status = Status(s);
        if (lease.OperationId is { } operation) s.Receipts[operation] = new(lease.ExpectedRevision, Serialize(status), null);
        return status;
    });
    public void Fail(SourcePreparationLease lease, GenerationException error) => Access(() =>
    {
        if (!sessions.TryGetValue(lease.PreparationId, out var s) || s.Active != lease) return 0;
        AddDiagnostics(s, [error.Message]); s.Revision++;
        if (error.StatusCode == 429)
        {
            var until = clock.GetUtcNow().AddSeconds(Math.Clamp(error.RetryAfterSeconds ?? 60, 1, 86_400));
            if (cooldown is null || until > cooldown) cooldown = until;
        }
        if (s.State != "Cancelled")
        {
            if (s.Index is null || error.Code is "book_has_content" or "edition_changed" or "book_not_found") s.State = "Cancelled";
            else if (s.Cursor < s.Sources.Length && error.StatusCode == 429) s.Attempts[s.Cursor]--;
            else if (s.Cursor < s.Sources.Length && s.Attempts[s.Cursor] >= 2)
            {
                s.UnitStates[s.Cursor++] = "Failed";
                if (s.Cursor == s.Sources.Length) s.State = "Partial";
            }
        }
        if (lease.OperationId is { } operation) s.Receipts[operation] = new(lease.ExpectedRevision, null, error);
        return 0;
    });
    public void Release(SourcePreparationLease lease) => Access(() =>
    {
        if (sessions.TryGetValue(lease.PreparationId, out var s) && s.Active == lease) s.Active = null;
        if (active == lease.PreparationId) active = null;
        return 0;
    });
    public SourcePreparationStatus Cancel(Guid owner, Guid bookId, Guid id)
    {
        CancellationTokenSource? cancellation = null;
        var status = Access(() =>
        {
            var s = Find(owner, bookId, id); s.State = "Cancelled"; s.Revision++;
            cancellation = s.Cancellation;
            return Status(s);
        });
        cancellation!.Cancel();
        return status;
    }

    // Keep the preparation locked until its sources have been copied to the generation store.
    public SourcePreparationLease BeginGenerate(Guid owner, Guid bookId, Guid id, SourcePreparationGenerateRequest request) => Access(() =>
    {
        var s = Find(owner, bookId, id); ValidateOperation(request.OperationId, request.ExpectedRevision);
        if (s.Active is not null || s.Revision != request.ExpectedRevision) throw Conflict();
        if (s.State is not ("Ready" or "Partial") || s.Index is null || s.Cursor != s.Sources.Length
            || !s.Sources.Any(source => source is not null))
            throw Error("sources_incomplete", "Conclua a coleta e obtenha ao menos uma fonte antes de gerar.", 409);
        if (!request.EditionConfirmed) throw Error("edition_not_confirmed", "Confirme a correspondência da obra e da edição antes de gerar.");
        if (active is not null) throw Busy();
        return Activate(s, request.OperationId, request.ExpectedRevision);
    });
    public Guid? ExistingGeneration(Guid owner, Guid bookId, Guid id) => Access(() => Find(owner, bookId, id).GenerationRunId);
    public void AttachGeneration(SourcePreparationLease lease, Guid runId) => Access(() =>
    {
        var s = ForLease(lease);
        s.TransferredUnits = Status(s).Units;
        s.GenerationRunId = runId; s.GenerateOperation = lease.OperationId; s.GenerateRevision = lease.ExpectedRevision;
        s.Revision++;
        // Generation owns the bounded source bytes now. Preserve only provenance summaries.
        s.Sources = new byte[]?[s.Sources.Length];
        return 0;
    });

    public void Stop()
    {
        CancellationTokenSource[] cancel;
        lock (sync)
        {
            stopping = true;
            cancel = sessions.Values.Select(s => s.Cancellation).ToArray();
            foreach (var s in sessions.Values) { s.State = "Cancelled"; s.Revision++; }
        }
        foreach (var source in cancel) source.Cancel();
    }

    private void CheckCooldown()
    {
        if (stopping) throw Error("preparation_stopping", "O servidor está encerrando.", 503);
        if (cooldown is { } at && at > clock.GetUtcNow())
            throw new GenerationException("source_rate_limited", "A fonte pediu uma pausa. Aguarde antes de retomar a coleta.",
                429, (int)Math.Ceiling((at - clock.GetUtcNow()).TotalSeconds));
    }

    private SourcePreparationLease Activate(Session s, Guid? operation, int revision)
    {
        active = s.Id; s.Touched = clock.GetUtcNow();
        return s.Active = new(s.Id, operation, revision, s.Cancellation.Token);
    }
    private Session ForLease(SourcePreparationLease lease)
    {
        if (!sessions.TryGetValue(lease.PreparationId, out var s) || s.Active != lease || s.Expired || s.State == "Cancelled")
            throw Error("preparation_cancelled", "A coleta foi cancelada ou expirou; o material já obtido foi preservado.", 409);
        return s;
    }
    private Session Find(Guid owner, Guid bookId, Guid id)
    {
        if (!sessions.TryGetValue(id, out var s) || s.Owner != owner || s.BookId != bookId || s.Expired)
            throw Error("preparation_not_found", "Preparação não encontrada ou expirada.", 404);
        s.Touched = clock.GetUtcNow(); return s;
    }
    private SourcePreparationStatus Status(Session s)
    {
        var units = s.TransferredUnits ?? s.Index?.Chapters.Select((chapter, i) =>
        {
            var source = s.Sources[i] is { } bytes ? Deserialize<CollectedNarrativeSource>(bytes) : null;
            var text = source?.Text;
            return new SourcePreparationUnit(chapter.Order, chapter.Title, s.UnitStates[i], Encoding.UTF8.GetByteCount(text ?? ""),
                source?.Provenance.Url, source?.Provenance.RevisionId, source?.Provenance.SourceHash,
                text is null ? null : text[..Math.Min(160, text.Length)], source?.Provenance.Language);
        }).ToArray() ?? [];
        var language = NarrativeSourceCatalog.GetLanguage(s.Book);
        return new(s.Id, s.BookId, s.Revision, s.State, s.Index?.Book.Title ?? "Wikibooks", s.Index?.Book.Language ?? "pt",
            s.Index?.SourceUrl ?? "", Attribution, s.Index?.Book.License ?? "CC BY-SA 4.0", s.Cursor, units.Count,
            units, [.. s.Diagnostics], Min(s.Touched.AddMinutes(30), s.Created.AddHours(2)), s.GenerationRunId,
            language.Language, language.Inferred, language.Source);
    }
    public const string Attribution = "Colaboradores do Wikibooks — CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/). Sinopses organizadas por capítulo; extração/tradução assistida. Revisões e créditos por unidade no relatório.";
    private T Access<T>(Func<T> operation)
    {
        var cancel = new List<CancellationTokenSource>();
        try
        {
            lock (sync)
            {
                var now = clock.GetUtcNow();
                foreach (var s in sessions.Values.ToArray())
                    if (s.Expired || now >= s.Touched.AddMinutes(30) || now >= s.Created.AddHours(2))
                    {
                        s.Expired = true; cancel.Add(s.Cancellation);
                        if (s.Active is null) sessions.Remove(s.Id);
                    }
                return operation();
            }
        }
        finally { foreach (var c in cancel) { try { c.Cancel(); } catch (ObjectDisposedException) { } } }
    }
    private static void CheckRetained(Session s, long extra = 0)
    {
        var size = s.Sources.Sum(b => (long)(b?.Length ?? 0)) + s.Receipts.Values.Sum(r => (long)(r.Status?.Length ?? 0) + 1024)
            + Serialize(s.Diagnostics).LongLength + Serialize(s.TransferredUnits).LongLength
            + (s.Index is null ? 0 : Serialize(s.Index).LongLength) + extra + 64 * 1024;
        if (size > MaxBytes) throw Error("preparation_too_large", "A preparação excedeu o limite de 4 MiB. Preserve o material parcial.", 422);
    }
    private static void AddDiagnostics(Session s, IEnumerable<string> values) => s.Diagnostics = s.Diagnostics.Concat(values)
        .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v[..Math.Min(v.Length, 500)]).Distinct().TakeLast(100).ToList();
    private static void ValidateOperation(Guid operation, int revision)
    { if (operation == Guid.Empty || revision < 0) throw Error("invalid_operation", "Informe operationId e revisão válidos."); }
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static byte[] Serialize<T>(T item) => JsonSerializer.SerializeToUtf8Bytes(item, Json);
    private static T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json)!;
    private static GenerationException Error(string code, string message, int status = 400) => new(code, message, status);
    private static GenerationException Conflict() => Error("preparation_conflict", "A coleta mudou ou possui uma operação em andamento. Consulte o progresso.", 409);
    private static GenerationException Busy() => new("source_busy", "Outra leitura de fonte está em andamento. Aguarde e retome a coleta.", 429, 10);
}
