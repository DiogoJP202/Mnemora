namespace Mnemora.Application;

public sealed record SourceBookIdentity(Guid BookId, string Title, string Author,
    string? Isbn10, string? Isbn13, string? Language, string? Publisher,
    DateOnly? PublishedDate, string? ExternalProvider, string? ExternalId);

// Reference discovery is editorial material only; it is never a memory-pack input.
public sealed record BookSourceCandidate(string Provider, string ExternalId, string Title,
    string Url, string Kind, string Match, string? Language, string Attribution,
    DateTimeOffset RetrievedAt, string ResponseHash, bool RequiresReview = true);

public sealed record BookSourceDiscoveryResult(SourceBookIdentity Book,
    DateTimeOffset RetrievedAt, string OutlineStatus,
    IReadOnlyList<ExternalReadingOutlineItem> Outline,
    string NarrativeStatus, IReadOnlyList<BookSourceCandidate> Sources,
    IReadOnlyList<string> Diagnostics);

public sealed class SourceDiscoveryBusyException : Exception;
