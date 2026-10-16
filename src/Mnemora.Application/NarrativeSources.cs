namespace Mnemora.Application;

public sealed record NarrativeSourceBook(string Key, string Title, string IndexPage, string Language, string License);
public sealed record NarrativeChapter(int Order, string Title, string PageTitle);
public sealed record NarrativeSourceIndex(NarrativeSourceBook Book, IReadOnlyList<NarrativeChapter> Chapters,
    string RevisionId, string SourceUrl, IReadOnlyList<string> Diagnostics);
public sealed record CollectedNarrativeSource(int Order, string Title, string Text, GenerationSourceProvenance Provenance);
public sealed record GenerationSourceProvenance(string UnitSlug, string Provider, string Url, string RevisionId,
    string PageId, string Language, string License, string Attribution, DateTimeOffset RetrievedAt,
    string ResponseHash, string SourceHash);

public interface IBookNarrativeSourceProvider
{
    Task<NarrativeSourceIndex> GetIndexAsync(string bookKey, CancellationToken cancellationToken);
    Task<CollectedNarrativeSource?> GetChapterAsync(NarrativeSourceIndex index, NarrativeChapter chapter,
        CancellationToken cancellationToken);
}

public sealed record SourcePreparationUnit(int Order, string Title, string Status, int SummaryBytes,
    string? SourceUrl, string? RevisionId, string? SourceHash, string? SummaryPreview);
public sealed record SourcePreparationStatus(Guid PreparationId, Guid BookId, int Revision, string State,
    string SourceBookTitle, string Language, string SourceUrl, string Attribution, string License,
    int CompletedUnits, int TotalUnits, IReadOnlyList<SourcePreparationUnit> Units,
    IReadOnlyList<string> Diagnostics, DateTimeOffset ExpiresAt, Guid? GenerationRunId);
public sealed record SourcePreparationGenerateRequest(Guid OperationId, int ExpectedRevision,
    bool EditionConfirmed, bool IndexComplete);
public sealed record SourcePreparationAvailability(bool Supported, string? Reason,
    IReadOnlyList<string> SupportedTitles);
