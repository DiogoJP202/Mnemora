using Microsoft.Extensions.Options;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackGenerationSessionTests
{
    [Fact]
    public void Sources_and_document_are_owned_snapshots_and_wrong_owner_is_hidden()
    {
        var fixture = new Fixture();
        var sources = new[] { new GenerationSource("Chegada", "Lia chega.") };
        var start = fixture.Start with { Sources = sources };
        var document = fixture.Assembler.Create(fixture.Book, start);
        var state = fixture.Store.Create(fixture.Owner, fixture.BookId, start, document);
        sources[0] = new("Fonte alterada", "Outro livro.");
        var snapshot = fixture.Store.Snapshot(fixture.Owner, fixture.BookId, state.RunId);
        Assert.Equal("Lia chega.", snapshot.CurrentSource!.Summary);
        Assert.Equal("Chegada", snapshot.Document.ReadingUnits[0].Title);
        Assert.Equal(404, Assert.Throws<GenerationException>(() => fixture.Store.GetStatus(
            Guid.NewGuid(), fixture.BookId, state.RunId)).StatusCode);
        Assert.Equal(404, Assert.Throws<GenerationException>(() => fixture.Store.GetStatus(
            fixture.Owner, Guid.NewGuid(), state.RunId)).StatusCode);
    }

    [Fact]
    public void Global_slots_and_one_session_per_owner_are_reserved()
    {
        var fixture = new Fixture();
        fixture.Create();
        Assert.Equal("session_exists", Assert.Throws<GenerationException>(() => fixture.Create()).Code);
        fixture.Create(Guid.NewGuid());
        Assert.Equal("generation_capacity", Assert.Throws<GenerationException>(() => fixture.Create(Guid.NewGuid())).Code);
    }

    [Fact]
    public void Owner_lookup_can_find_another_book_without_exposing_other_admin_sessions()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        Assert.Equal(state.RunId, fixture.Store.FindExisting(fixture.Owner)!.RunId);
        Assert.Null(fixture.Store.FindExisting(fixture.Owner, Guid.NewGuid()));
        Assert.Null(fixture.Store.FindExisting(Guid.NewGuid()));
    }

    [Fact]
    public void Cancellation_keeps_provider_lease_until_actual_completion()
    {
        var fixture = new Fixture();
        var first = fixture.Create();
        var otherOwner = Guid.NewGuid();
        var other = fixture.Create(otherOwner);
        var lease = fixture.Begin(first);
        var cancelled = fixture.Store.Cancel(fixture.Owner, fixture.BookId, first.RunId);
        Assert.Equal("Cancelled", cancelled.State);
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal("session_exists", Assert.Throws<GenerationException>(() => fixture.Create()).Code);
        Assert.Equal("generation_busy", Assert.Throws<GenerationException>(() => fixture.Store.Begin(
            otherOwner, fixture.BookId, other.RunId, new(Guid.NewGuid(), other.Revision), true, 10)).Code);
        Assert.Equal("generation_cancelled", Assert.Throws<GenerationException>(() => fixture.Store.Complete(
            lease, fixture.Document, [], [], false)).Code);
        fixture.Store.Fail(lease, new("generation_cancelled", "Cancelada.", 409));
        fixture.Store.Release(lease);
        Assert.NotEqual(first.RunId, fixture.Create().RunId);
    }

    [Fact]
    public void Expiration_cancels_active_call_without_releasing_its_slot_early()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var lease = fixture.Begin(state);
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(404, Assert.Throws<GenerationException>(() => fixture.Store.GetStatus(
            fixture.Owner, fixture.BookId, state.RunId)).StatusCode);
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal("session_exists", Assert.Throws<GenerationException>(() => fixture.Create()).Code);
        fixture.Store.Release(lease);
        Assert.NotEqual(state.RunId, fixture.Create().RunId);
    }

    [Fact]
    public void Absolute_lifetime_expires_even_when_status_is_polled()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        for (var index = 0; index < 5; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(20));
            fixture.Store.GetStatus(fixture.Owner, fixture.BookId, state.RunId);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(404, Assert.Throws<GenerationException>(() => fixture.Store.GetStatus(
            fixture.Owner, fixture.BookId, state.RunId)).StatusCode);
    }

    [Fact]
    public void Same_inflight_operation_returns_progress_without_another_lease()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var request = new GenerationNextRequest(Guid.NewGuid(), state.Revision);
        var lease = fixture.Store.Begin(fixture.Owner, fixture.BookId, state.RunId, request, true, 10).Lease!;
        var repeat = fixture.Store.Begin(fixture.Owner, fixture.BookId, state.RunId, request, true, 10);
        Assert.Null(repeat.Lease);
        Assert.Equal("Processing", repeat.Receipt!.State);
        Assert.Equal(1, repeat.Receipt.Calls);
        Assert.Equal("operation_conflict", Assert.Throws<GenerationException>(() => fixture.Store.ResolveReceipt(
            fixture.Owner, fixture.BookId, state.RunId, request with { ExpectedRevision = 1 })).Code);
        fixture.Store.Fail(lease, new("failed", "Erro."));
        fixture.Store.Release(lease);
    }

    [Fact]
    public void Completed_receipts_are_kept_across_later_chapters_and_ids_cannot_be_reused()
    {
        var fixture = new Fixture(units: 3);
        var state = fixture.Create();
        var request = new GenerationNextRequest(Guid.NewGuid(), state.Revision);
        var lease = fixture.Store.Begin(fixture.Owner, fixture.BookId, state.RunId, request, true, 10).Lease!;
        fixture.Store.RecordUsage(lease, 10, 20);
        var first = fixture.Store.Complete(lease, fixture.Document, [], [], false);
        fixture.Store.Release(lease);
        var secondLease = fixture.Begin(first);
        fixture.Store.RecordUsage(secondLease, 10, 20);
        fixture.Store.Complete(secondLease, fixture.Document, [], [], false);
        fixture.Store.Release(secondLease);
        var repeated = fixture.Store.ResolveReceipt(fixture.Owner, fixture.BookId, state.RunId, request)!;
        Assert.Equal(first.Revision, repeated.Revision);
        Assert.Equal(1, repeated.CompletedUnits);
        Assert.Equal(1, repeated.Calls);
        Assert.Equal("operation_conflict", Assert.Throws<GenerationException>(() => fixture.Store.ResolveReceipt(
            fixture.Owner, fixture.BookId, state.RunId, request with { ExpectedRevision = 2 })).Code);
    }

    [Fact]
    public void Failed_receipt_replays_error_and_unknown_usage_remains_reserved()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var request = new GenerationNextRequest(Guid.NewGuid(), state.Revision);
        var lease = fixture.Store.Begin(fixture.Owner, fixture.BookId, state.RunId, request, true, 10).Lease!;
        fixture.Store.Fail(lease, new("provider_timeout", "Timeout controlado.", 503));
        fixture.Store.Release(lease);
        var status = fixture.Store.GetStatus(fixture.Owner, fixture.BookId, state.RunId);
        Assert.Equal("Paused", status.State);
        Assert.Equal(110, status.Tokens);
        Assert.Equal(110, status.EstimatedTokens);
        Assert.Equal(1, status.Revision);
        Assert.Equal("provider_timeout", Assert.Throws<GenerationException>(() => fixture.Store.ResolveReceipt(
            fixture.Owner, fixture.BookId, state.RunId, request)).Code);
        Assert.Equal(1, fixture.Store.GetStatus(fixture.Owner, fixture.BookId, state.RunId).Calls);
    }

    [Fact]
    public void Provider_retry_after_is_global_and_survives_new_sessions()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var lease = fixture.Begin(state);
        fixture.Store.Fail(lease, new("provider_rate_limited", "Quota.", 429, 900));
        fixture.Store.Release(lease);
        fixture.Store.Cancel(fixture.Owner, fixture.BookId, state.RunId);
        var replacement = fixture.Create();
        var error = Assert.Throws<GenerationException>(() => fixture.Begin(replacement));
        Assert.Equal("generation_rate_limited", error.Code);
        Assert.Equal(900, error.RetryAfterSeconds);
        var anotherOwner = Guid.NewGuid();
        var other = fixture.Create(anotherOwner);
        Assert.Equal("generation_rate_limited", Assert.Throws<GenerationException>(() => fixture.Store.Begin(
            anotherOwner, fixture.BookId, other.RunId, new(Guid.NewGuid(), other.Revision), true, 10)).Code);
    }

    [Fact]
    public void Daily_budget_is_not_reset_by_cancellation_replacement_or_other_admin()
    {
        var fixture = new Fixture(maxTokens: 200);
        var state = fixture.Create();
        var lease = fixture.Begin(state);
        fixture.Store.Fail(lease, new("provider_timeout", "Falha desconhecida.", 503));
        fixture.Store.Release(lease);
        fixture.Store.Cancel(fixture.Owner, fixture.BookId, state.RunId);
        var replacement = fixture.Create();
        Assert.Equal("generation_token_limit", Assert.Throws<GenerationException>(() => fixture.Begin(replacement)).Code);
        var otherOwner = Guid.NewGuid();
        var other = fixture.Create(otherOwner);
        Assert.Equal("generation_token_limit", Assert.Throws<GenerationException>(() => fixture.Store.Begin(
            otherOwner, fixture.BookId, other.RunId, new(Guid.NewGuid(), other.Revision), true, 10)).Code);
        fixture.Clock.Advance(TimeSpan.FromHours(24));
        var fresh = fixture.Create();
        var freshLease = fixture.Begin(fresh);
        fixture.Store.Fail(freshLease, new("test_end", "Encerrar."));
        fixture.Store.Release(freshLease);
    }

    [Fact]
    public void Known_usage_reconciles_reservation_once_and_rejects_invalid_usage()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var lease = fixture.Begin(state);
        Assert.Equal("invalid_usage", Assert.Throws<GenerationException>(() => fixture.Store.RecordUsage(lease, -1, 20)).Code);
        fixture.Store.RecordUsage(lease, 20, 30);
        fixture.Store.RecordUsage(lease, 200, 300);
        var status = fixture.Store.Complete(lease, fixture.Document, [], [], false);
        fixture.Store.Release(lease);
        Assert.Equal(50, status.Tokens);
        Assert.Equal(0, status.EstimatedTokens);
        Assert.Equal(50, fixture.Store.Snapshot(fixture.Owner, fixture.BookId, state.RunId).Report.Tokens);
    }

    [Fact]
    public void Missing_sources_do_not_consume_provider_quota_and_remain_partial()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var lease = fixture.Store.Begin(fixture.Owner, fixture.BookId, state.RunId,
            new(Guid.NewGuid(), state.Revision), false, 0).Lease!;
        var completed = fixture.Store.Complete(lease, fixture.Document, [], [], true);
        fixture.Store.Release(lease);
        Assert.Equal("Completed", completed.State);
        Assert.Equal("MissingSource", completed.Coverage[0].Status);
        Assert.Equal(0, completed.Tokens);
        Assert.Equal(0, completed.Calls);
        Assert.True(fixture.Store.Snapshot(fixture.Owner, fixture.BookId, state.RunId).Report.Partial);
        Assert.False(completed.HasImportableResult);
    }

    [Fact]
    public void Oversized_candidate_is_not_committed_and_failure_metadata_keeps_prefix_downloadable()
    {
        var fixture = new Fixture(units: 2);
        var state = fixture.Create();
        var firstLease = fixture.Begin(state);
        fixture.Store.RecordUsage(firstLease, 10, 10);
        var prefix = fixture.Store.Complete(firstLease, fixture.Document, [], [], false);
        fixture.Store.Release(firstLease);
        var nextLease = fixture.Begin(prefix);
        var huge = fixture.Document with { Entities = [new MemoryPackEntity("huge", "Character", "Lia",
            new string('x', fixture.Options.MaxDocumentBytes), null, "capitulo-001", 1, null, [], [])] };
        var error = Assert.Throws<GenerationException>(() => fixture.Store.Complete(nextLease, huge, [], [], false));
        fixture.Store.Fail(nextLease, error);
        fixture.Store.Release(nextLease);
        var snapshot = fixture.Store.Snapshot(fixture.Owner, fixture.BookId, state.RunId);
        Assert.Empty(snapshot.Document.Entities);
        Assert.Equal(1, snapshot.Status.CompletedUnits);
        Assert.Equal("Failed", snapshot.Report.Units[1].Status);
        Assert.Equal("Paused", snapshot.Status.State);
        Assert.Contains(error.Message, snapshot.Report.Diagnostics);
    }

    [Fact]
    public void Failed_replacement_keeps_previous_terminal_result()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        fixture.Store.Cancel(fixture.Owner, fixture.BookId, state.RunId);
        var huge = fixture.Document with { Source = new(null, new string('x', fixture.Options.MaxDocumentBytes)) };
        Assert.Equal("document_too_large", Assert.Throws<GenerationException>(() => fixture.Store.Create(
            fixture.Owner, fixture.BookId, fixture.Start, huge)).Code);
        Assert.Equal(state.RunId, fixture.Store.GetStatus(fixture.Owner, fixture.BookId, state.RunId).RunId);
    }

    [Fact]
    public void Shutdown_signals_calls_and_prevents_new_commit()
    {
        var fixture = new Fixture();
        var state = fixture.Create();
        var lease = fixture.Begin(state);
        fixture.Store.Stop();
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal("generation_cancelled", Assert.Throws<GenerationException>(() => fixture.Store.Complete(
            lease, fixture.Document, [], [], false)).Code);
        fixture.Store.Fail(lease, new("generation_cancelled", "Encerramento.", 409));
        fixture.Store.Release(lease);
        Assert.Equal("Cancelled", fixture.Store.GetStatus(fixture.Owner, fixture.BookId, state.RunId).State);
        Assert.Equal("generation_stopping", Assert.Throws<GenerationException>(() => fixture.Create()).Code);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private sealed class Fixture
    {
        public GenerationOptions Options { get; }
        public Clock Clock { get; } = new();
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public MemoryPackBook Book { get; } = new("As cartas do cais", "Autora original");
        public GenerationStartRequest Start { get; }
        public MemoryPackGenerationAssembler Assembler { get; }
        public MemoryPackDocument Document { get; }
        public MemoryPackGenerationSessionStore Store { get; }

        public Fixture(int units = 1, int maxTokens = 180_000)
        {
            Options = new GenerationOptions { MaxOutputTokens = 100, MinCallIntervalSeconds = 0, MaxTokens = maxTokens };
            Start = new(Enumerable.Range(1, units).Select(index =>
                new GenerationSource($"Chegada {index}", "Lia chega ao cais.")).ToArray(),
                "Resumos originais de teste", EditionConfirmed: true, IndexComplete: true);
            Assembler = new(Options);
            Document = Assembler.Create(Book, Start);
            Store = new(Microsoft.Extensions.Options.Options.Create(Options), Clock);
        }
        public GenerationSessionStatus Create(Guid? owner = null) => Store.Create(owner ?? Owner, BookId, Start, Document);
        public GenerationSessionLease Begin(GenerationSessionStatus state) => Store.Begin(Owner, BookId,
            state.RunId, new(Guid.NewGuid(), state.Revision), true, 10).Lease!;
    }
}
