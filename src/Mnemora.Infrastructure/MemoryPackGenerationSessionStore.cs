using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed record GenerationSessionSnapshot(GenerationSessionStatus Status, MemoryPackDocument Document,
    GenerationReport Report, GenerationSource? CurrentSource);

public sealed class GenerationSessionLease
{
    internal Guid LeaseId { get; } = Guid.NewGuid();
    internal Guid OperationId { get; }
    internal int ExpectedRevision { get; }
    internal int ReservedTokens { get; }
    internal DateTimeOffset BudgetWindow { get; }
    internal bool UsageRecorded { get; set; }
    internal CancellationTokenSource Cancellation { get; } = new();
    public Guid RunId { get; }
    public bool UsesProvider { get; }
    public CancellationToken CancellationToken => Cancellation.Token;

    internal GenerationSessionLease(Guid runId, GenerationNextRequest request, bool usesProvider,
        int reservedTokens, DateTimeOffset budgetWindow)
    {
        RunId = runId;
        OperationId = request.OperationId;
        ExpectedRevision = request.ExpectedRevision;
        UsesProvider = usesProvider;
        ReservedTokens = reservedTokens;
        BudgetWindow = budgetWindow;
    }
}

public sealed record GenerationStepStart(GenerationSessionLease? Lease, GenerationSessionStatus? Receipt);

// This store owns all mutable execution state. It never captures a scoped DbContext.
// Large retained blocks and receipts are bounded UTF-8, not duplicated object graphs.
public sealed class MemoryPackGenerationSessionStore(IOptions<GenerationOptions> options, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly GenerationOptions limits = options.Value;
    private readonly object sync = new();
    private readonly Dictionary<Guid, Session> sessions = [];
    private Guid? activeRun;
    private DateTimeOffset? lastCallAt;
    private DateTimeOffset? globalCooldown;
    private DateTimeOffset budgetWindow = time.GetUtcNow();
    private long globalTokens;
    private bool stopping;

    private sealed record Receipt(int ExpectedRevision, byte[]? Status, string? Code = null,
        string? Message = null, int StatusCode = 400, int? RetryAfterSeconds = null);

    private sealed class Session
    {
        public required Guid Id { get; init; }
        public required Guid Owner { get; init; }
        public required Guid BookId { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset TouchedAt { get; set; }
        public required byte[] Sources { get; init; }
        public required byte[] SourceProvenance { get; init; }
        public required byte[] Document { get; set; }
        public required byte[] Report { get; set; }
        public required GenerationUnitCoverage[] Coverage { get; set; }
        public required int[] Attempts { get; init; }
        public required bool IndexComplete { get; init; }
        public required string? Attribution { get; init; }
        public required string? SourceUrl { get; init; }
        public string State { get; set; } = "Ready";
        public int Revision { get; set; }
        public int Cursor { get; set; }
        public int Calls { get; set; }
        public long Tokens { get; set; }
        public long EstimatedTokens { get; set; }
        public bool HasEntities { get; set; }
        public bool Expired { get; set; }
        public DateTimeOffset? NextAllowedAt { get; set; }
        public GenerationSessionLease? Active { get; set; }
        public List<string> Diagnostics { get; set; } = [];
        public Dictionary<Guid, Receipt> Receipts { get; } = [];
    }

    public GenerationSessionStatus Create(Guid owner, Guid bookId, GenerationStartRequest start,
        MemoryPackDocument document, IReadOnlyList<string>? diagnostics = null,
        IReadOnlyList<GenerationSourceProvenance>? provenance = null) => Access(() =>
    {
        if (stopping) throw Error("generation_stopping", "O servidor está encerrando. Preserve o resultado parcial.", 503);
        var previous = sessions.Values.FirstOrDefault(session => session.Owner == owner);
        if (previous is not null)
        {
            if (previous.Active is not null || previous.State is not ("Completed" or "Cancelled"))
                throw Error("session_exists", "Há uma sessão em andamento. Retome ou cancele antes de iniciar outra.", 409);
        }
        var retainedSessions = sessions.Count - (previous is null ? 0 : 1);
        if (retainedSessions >= limits.MaxSessions
            || (retainedSessions + 1L) * limits.MaxSessionBytes > limits.MaxGlobalBytes)
            throw Error("generation_capacity", "O limite de sessões temporárias foi atingido. Aguarde uma sessão terminar.", 503);

        var assembler = new MemoryPackGenerationAssembler(limits);
        var coverage = start.Sources.Select((source, index) =>
        {
            var input = assembler.BuildInput(document, source, index);
            return new GenerationUnitCoverage(input.UnitSlug, document.ReadingUnits[index].Title,
                "Pending", Hash(Encoding.UTF8.GetBytes(string.Join("\n\n", input.Paragraphs.Select(p => p.Text)))));
        }).ToArray();
        var now = time.GetUtcNow();
        var session = new Session
        {
            Id = Guid.NewGuid(), Owner = owner, BookId = bookId, CreatedAt = now, TouchedAt = now,
            Sources = Serialize(start.Sources), SourceProvenance = Serialize(provenance ?? []),
            Document = Serialize(document), Report = [],
            Coverage = coverage, Attempts = new int[coverage.Length], IndexComplete = start.IndexComplete,
            Attribution = document.Source?.Attribution, SourceUrl = document.Source?.Url,
            Diagnostics = CleanDiagnostics(diagnostics ?? [])
        };
        session.Report = BuildReport(session, document, []);
        CheckRetained(session, session.Document, session.Report, reserveFailure: true);
        if (previous is not null) sessions.Remove(previous.Id);
        sessions.Add(session.Id, session);
        return Status(session);
    });

    public GenerationSessionStatus? FindExisting(Guid owner, Guid? bookId = null) => Access(() =>
    {
        var session = sessions.Values.FirstOrDefault(s => s.Owner == owner
            && (bookId is null || s.BookId == bookId.Value) && !s.Expired);
        if (session is null) return null;
        Touch(session);
        return Status(session);
    });

    public GenerationSessionStatus GetStatus(Guid owner, Guid bookId, Guid runId) => Access(() =>
    {
        var session = Find(owner, bookId, runId);
        Touch(session);
        return Status(session);
    });

    public GenerationSessionSnapshot Snapshot(Guid owner, Guid bookId, Guid runId) => Access(() =>
    {
        var session = Find(owner, bookId, runId);
        Touch(session);
        var document = Deserialize<MemoryPackDocument>(session.Document);
        var report = CurrentReport(session);
        GenerationSource? source = null;
        if (session.Cursor < session.Coverage.Length)
        {
            using var sources = JsonDocument.Parse(session.Sources);
            source = sources.RootElement[session.Cursor].Deserialize<GenerationSource>(Json)!;
        }
        return new GenerationSessionSnapshot(Status(session), document, report, source);
    });

    public GenerationSessionStatus? ResolveReceipt(Guid owner, Guid bookId, Guid runId,
        GenerationNextRequest request) => Access(() => ReceiptStatus(Find(owner, bookId, runId), request));

    public GenerationStepStart Begin(Guid owner, Guid bookId, Guid runId, GenerationNextRequest request,
        bool usesProvider, int inputTokens) => Access(() =>
    {
        var session = Find(owner, bookId, runId);
        var receipt = ReceiptStatus(session, request);
        if (receipt is not null) return new GenerationStepStart(null, receipt);
        if (stopping) throw Error("generation_stopping", "O servidor está encerrando. Nenhuma nova chamada será iniciada.", 503);
        if (request.OperationId == Guid.Empty || request.ExpectedRevision < 0)
            throw Error("invalid_operation", "Informe uma operação e revisão válidas.");
        if (session.Revision != request.ExpectedRevision)
            throw Error("revision_conflict", "A sessão mudou. Consulte o progresso antes de continuar.", 409);
        if (session.Active is not null)
        {
            if (session.Active.OperationId == request.OperationId
                && session.Active.ExpectedRevision == request.ExpectedRevision)
                return new GenerationStepStart(null, Status(session));
            throw Error("generation_processing", "Esta unidade já está sendo processada.", 409);
        }
        if (session.State is "Completed" or "Cancelled")
            throw Error("generation_finished", "Esta sessão já foi encerrada.", 409);
        if (session.Cursor >= session.Coverage.Length)
            throw Error("generation_finished", "Todas as unidades já foram processadas.", 409);
        if (session.Receipts.Count >= limits.MaxCalls + limits.MaxUnits)
            throw Error("operation_limit", "O limite de operações desta sessão foi atingido.", 429);
        CheckRetained(session, session.Document, session.Report, reserveFailure: true);

        var now = time.GetUtcNow();
        ResetBudget(now);
        var reserved = 0;
        if (usesProvider)
        {
            if (inputTokens < 0 || inputTokens > limits.MaxInputTokens)
                throw Error("provider_input_limit", "O resumo, schema e registro conhecido excedem o limite de entrada. Não serão truncados.", 422);
            if (activeRun.HasValue)
                throw Error("generation_busy", "Outra unidade está usando o provedor. Aguarde e tente novamente.", 409);
            if (session.Attempts[session.Cursor] >= limits.MaxAttemptsPerUnit || session.Calls >= limits.MaxCalls)
                throw Error("attempts_exhausted", "O limite de tentativas foi atingido. Baixe o resultado parcial.", 429);
            var next = Later(globalCooldown, Later(session.NextAllowedAt, lastCallAt?.AddSeconds(limits.MinCallIntervalSeconds)));
            if (next > now)
                throw Error("generation_rate_limited", "Aguarde o intervalo operacional antes da próxima unidade.", 429,
                    Math.Max(1, (int)Math.Ceiling((next.Value - now).TotalSeconds)));
            reserved = checked(inputTokens + limits.MaxOutputTokens);
            if (session.Tokens + reserved > limits.MaxTokens || globalTokens + reserved > limits.MaxTokens)
                throw Error("generation_token_limit", "O orçamento operacional temporário foi atingido. Baixe o resultado parcial.", 429);
            session.Attempts[session.Cursor]++;
            session.Calls++;
            session.Tokens += reserved;
            session.EstimatedTokens += reserved;
            globalTokens += reserved;
            lastCallAt = now;
            activeRun = session.Id;
        }
        var lease = new GenerationSessionLease(session.Id, request, usesProvider, reserved, budgetWindow);
        session.Active = lease;
        session.State = "Processing";
        Touch(session);
        return new GenerationStepStart(lease, null);
    });

    public void RecordUsage(GenerationSessionLease lease, int inputTokens, int outputTokens) => Access(() =>
    {
        var session = FindLease(lease);
        if (lease.UsageRecorded) return true;
        if (!lease.UsesProvider || inputTokens < 0 || outputTokens < 0
            || (long)inputTokens + outputTokens > int.MaxValue)
            throw Error("invalid_usage", "O provedor informou consumo inválido.", 503);
        var actual = (long)inputTokens + outputTokens;
        var adjustment = actual - lease.ReservedTokens;
        session.Tokens += adjustment;
        session.EstimatedTokens -= lease.ReservedTokens;
        if (budgetWindow == lease.BudgetWindow) globalTokens += adjustment;
        lease.UsageRecorded = true;
        return true;
    });

    public GenerationSessionStatus Complete(GenerationSessionLease lease, MemoryPackDocument document,
        IReadOnlyList<GenerationReportEvidence> evidence, IReadOnlyList<string> diagnostics,
        bool missingSource) => Access(() =>
    {
        var session = FindLease(lease);
        if (session.Expired || session.State == "Cancelled" || lease.CancellationToken.IsCancellationRequested)
            throw Error("generation_cancelled", "A sessão foi cancelada; o último resultado concluído foi preservado.", 409);
        var oldCoverage = session.Coverage;
        var oldDiagnostics = session.Diagnostics;
        var oldCursor = session.Cursor;
        var oldRevision = session.Revision;
        var oldState = session.State;
        try
        {
            session.Coverage = [.. session.Coverage];
            session.Coverage[session.Cursor] = session.Coverage[session.Cursor] with
            { Status = missingSource ? "MissingSource" : "Generated" };
            session.Cursor++;
            session.Revision++;
            session.Diagnostics = CleanDiagnostics([.. session.Diagnostics, .. diagnostics]);
            if (missingSource) session.Diagnostics = CleanDiagnostics([.. session.Diagnostics,
                "Unidade sem fonte: nenhum conteúdo foi inventado; confira a lacuna antes de importar."]);
            if (session.Tokens > limits.MaxTokens || globalTokens > limits.MaxTokens)
                session.Diagnostics = CleanDiagnostics([.. session.Diagnostics,
                    "O consumo medido superou a reserva operacional. Novas chamadas estão bloqueadas."]);
            session.State = session.Cursor == session.Coverage.Length ? "Completed" : "Ready";
            var bytes = Serialize(document);
            var report = BuildReport(session, document, evidence);
            // Reserve room for bounded failure/cancellation metadata after a completed prefix.
            if (report.Length > limits.MaxReportBytes - Math.Min(8 * 1024, limits.MaxReportBytes / 8))
                throw Error("report_too_large", "O relatório atingiu o limite reservado. Baixe o resultado parcial.", 422);
            session.HasEntities = document.Entities.Count > 0;
            var status = Status(session);
            var receipt = new Receipt(lease.ExpectedRevision, Serialize(status));
            CheckRetained(session, bytes, report, receipt, true);
            session.Document = bytes;
            session.Report = report;
            session.Receipts.Add(lease.OperationId, receipt);
            Touch(session);
            return status;
        }
        catch
        {
            session.Coverage = oldCoverage;
            session.Diagnostics = oldDiagnostics;
            session.Cursor = oldCursor;
            session.Revision = oldRevision;
            session.State = oldState;
            session.HasEntities = Deserialize<MemoryPackDocument>(session.Document).Entities.Count > 0;
            throw;
        }
    });

    public void Fail(GenerationSessionLease lease, GenerationException error) => Access(() =>
    {
        var session = FindLease(lease);
        if (session.Receipts.ContainsKey(lease.OperationId)) return true;
        session.Revision++;
        if (session.State != "Cancelled")
            session.State = error.Code is "edition_changed" or "book_has_content" or "book_not_found"
                ? "Cancelled" : "Paused";
        if (session.Cursor < session.Coverage.Length)
            session.Coverage[session.Cursor] = session.Coverage[session.Cursor] with { Status = "Failed" };
        session.Diagnostics = CleanDiagnostics([.. session.Diagnostics, error.Message]);
        if (error.RetryAfterSeconds is > 0)
        {
            session.NextAllowedAt = time.GetUtcNow().AddSeconds(error.RetryAfterSeconds.Value);
            globalCooldown = Later(globalCooldown, session.NextAllowedAt);
        }
        session.Receipts.Add(lease.OperationId, new Receipt(lease.ExpectedRevision, null,
            error.Code, error.Message, error.StatusCode, error.RetryAfterSeconds));
        RefreshReportBounded(session);
        Touch(session);
        return true;
    });

    public void Release(GenerationSessionLease lease)
    {
        lock (sync)
        {
            if (sessions.TryGetValue(lease.RunId, out var session) && session.Active?.LeaseId == lease.LeaseId)
            {
                session.Active = null;
                if (session.Expired) sessions.Remove(session.Id);
            }
            if (activeRun == lease.RunId) activeRun = null;
        }
        lease.Cancellation.Dispose();
    }

    public GenerationSessionStatus Cancel(Guid owner, Guid bookId, Guid runId, string? reason = null)
    {
        CancellationTokenSource? cancel = null;
        var status = Access(() =>
        {
            var session = Find(owner, bookId, runId);
            if (session.State != "Cancelled")
            {
                session.State = "Cancelled";
                session.Revision++;
                session.Diagnostics = CleanDiagnostics([.. session.Diagnostics,
                    reason ?? "Sessão cancelada pelo administrador."]);
                RefreshReportBounded(session);
            }
            Touch(session);
            cancel = session.Active?.Cancellation;
            return Status(session);
        });
        SafeCancel(cancel);
        return status;
    }

    private T Access<T>(Func<T> action)
    {
        List<CancellationTokenSource> cancel = [];
        try
        {
            lock (sync)
            {
                cancel = Prune();
                return action();
            }
        }
        finally { foreach (var source in cancel) SafeCancel(source); }
    }

    private List<CancellationTokenSource> Prune()
    {
        var now = time.GetUtcNow();
        var cancel = new List<CancellationTokenSource>();
        foreach (var session in sessions.Values.ToArray())
        {
            if (now - session.CreatedAt < TimeSpan.FromMinutes(limits.LifetimeMinutes)
                && now - session.TouchedAt < TimeSpan.FromMinutes(limits.IdleMinutes)) continue;
            if (session.Active is null) sessions.Remove(session.Id);
            else if (!session.Expired)
            {
                session.Expired = true;
                session.State = "Cancelled";
                session.Revision++;
                cancel.Add(session.Active.Cancellation);
            }
        }
        return cancel;
    }

    private Session Find(Guid owner, Guid bookId, Guid runId)
    {
        if (!sessions.TryGetValue(runId, out var session) || session.Owner != owner
            || session.BookId != bookId || session.Expired)
            throw Error("generation_not_found", "Sessão não encontrada ou expirada.", 404);
        return session;
    }

    private Session FindLease(GenerationSessionLease lease)
    {
        if (!sessions.TryGetValue(lease.RunId, out var session) || session.Active?.LeaseId != lease.LeaseId)
            throw Error("generation_not_found", "A operação não pertence a uma sessão ativa.", 404);
        return session;
    }

    private static GenerationSessionStatus? ReceiptStatus(Session session, GenerationNextRequest request)
    {
        if (session.Active?.OperationId == request.OperationId)
        {
            if (session.Active.ExpectedRevision != request.ExpectedRevision)
                throw Error("operation_conflict", "Este identificador já pertence a outra revisão.", 409);
        }
        if (!session.Receipts.TryGetValue(request.OperationId, out var receipt)) return null;
        if (receipt.ExpectedRevision != request.ExpectedRevision)
            throw Error("operation_conflict", "Este identificador já pertence a outra revisão.", 409);
        if (receipt.Status is not null) return Deserialize<GenerationSessionStatus>(receipt.Status);
        throw Error(receipt.Code!, receipt.Message!, receipt.StatusCode, receipt.RetryAfterSeconds);
    }

    private GenerationSessionStatus Status(Session session)
    {
        var next = Later(globalCooldown, Later(session.NextAllowedAt, lastCallAt?.AddSeconds(limits.MinCallIntervalSeconds)));
        if (next <= time.GetUtcNow()) next = null;
        return new(session.Id, session.BookId, session.Revision, session.State, session.Cursor,
            session.Coverage.Length, session.Calls, Counter(session.Tokens), Counter(session.EstimatedTokens),
            next, [.. session.Coverage], session.Diagnostics.TakeLast(20).ToArray(), session.HasEntities);
    }

    private byte[] BuildReport(Session session, MemoryPackDocument document,
        IReadOnlyList<GenerationReportEvidence> evidence) => Serialize(new GenerationReport(
        limits.Provider, limits.Model, GenerationPrompt.Version, session.Attribution, session.SourceUrl,
        session.IndexComplete, !session.IndexComplete || session.Coverage.Any(unit => unit.Status != "Generated"),
        Hash(Serialize(document)), session.Calls, Counter(session.Tokens), Counter(session.EstimatedTokens),
        [.. session.Coverage], evidence, [.. session.Diagnostics],
        Deserialize<GenerationSourceProvenance[]>(session.SourceProvenance)));

    private GenerationReport CurrentReport(Session session)
    {
        var previous = Deserialize<GenerationReport>(session.Report);
        return previous with
        {
            Calls = session.Calls, Tokens = Counter(session.Tokens), EstimatedTokens = Counter(session.EstimatedTokens),
            Units = [.. session.Coverage], Partial = !session.IndexComplete || session.Coverage.Any(unit => unit.Status != "Generated"),
            Diagnostics = [.. session.Diagnostics]
        };
    }

    private void RefreshReportBounded(Session session)
    {
        var previous = Deserialize<GenerationReport>(session.Report);
        var report = BuildReport(session, Deserialize<MemoryPackDocument>(session.Document), previous.Evidence);
        while (report.Length > limits.MaxReportBytes && session.Diagnostics.Count > 0)
        {
            session.Diagnostics.RemoveAt(0);
            report = BuildReport(session, Deserialize<MemoryPackDocument>(session.Document), previous.Evidence);
        }
        // Completed reports reserve headroom. Do not let reporting abort lease cleanup.
        if (report.Length > limits.MaxReportBytes) return;
        session.Report = report;
    }

    public void Stop()
    {
        CancellationTokenSource[] cancel;
        lock (sync)
        {
            stopping = true;
            cancel = sessions.Values.Where(session => session.Active is not null)
                .Select(session => session.Active!.Cancellation).ToArray();
            foreach (var session in sessions.Values.Where(session => session.Active is not null))
            { session.State = "Cancelled"; session.Revision++; }
        }
        foreach (var source in cancel) SafeCancel(source);
    }

    private void CheckRetained(Session session, byte[] document, byte[] report, Receipt? extra = null,
        bool reserveFailure = false)
    {
        if (document.Length > limits.MaxDocumentBytes)
            throw Error("document_too_large", "O pacote excede o limite do JSON v1.", 422);
        if (report.Length > limits.MaxReportBytes)
            throw Error("report_too_large", "O relatório excede o limite permitido.", 422);
        long retained = session.Sources.Length + session.SourceProvenance.LongLength + document.Length + report.Length + 32 * 1024
            + Serialize(session.Coverage).LongLength * 2
            + session.Diagnostics.Sum(diagnostic => (long)Encoding.UTF8.GetByteCount(diagnostic) * 2 + 32)
            + session.Receipts.Values.Sum(ReceiptBytes)
            + (extra is null ? 0 : ReceiptBytes(extra))
            + (reserveFailure ? Math.Min(8 * 1024, limits.MaxSessionBytes / 8) : 0);
        if (retained > limits.MaxSessionBytes)
            throw Error("session_too_large", "A sessão excede o limite de memória. Baixe o resultado parcial.", 422);
    }
    private static long ReceiptBytes(Receipt receipt) => (long)(receipt.Status?.Length ?? 0) + 256
        + Encoding.UTF8.GetByteCount(receipt.Message ?? "") * 2L
        + Encoding.UTF8.GetByteCount(receipt.Code ?? "") * 2L;

    private void ResetBudget(DateTimeOffset now)
    {
        if (now - budgetWindow >= TimeSpan.FromHours(24)) { budgetWindow = now; globalTokens = 0; }
    }
    private void Touch(Session session) => session.TouchedAt = time.GetUtcNow();
    private static int Counter(long value) => (int)Math.Min(int.MaxValue, Math.Max(0, value));
    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;
    private static List<string> CleanDiagnostics(IEnumerable<string> diagnostics) => diagnostics
        .Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Length > 1_000 ? text[..1_000] : text)
        .Distinct(StringComparer.Ordinal).Take(500).ToList();
    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json)!;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static GenerationException Error(string code, string message, int status = 400, int? retry = null) =>
        new(code, message, status, retry);
    private static void SafeCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
