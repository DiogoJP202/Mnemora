namespace Mnemora.Application;

public sealed record NarrativeSourceBook(string Key, string Title, string IndexPage, string Language, string License);
public sealed record NarrativeChapter(int Order, string Title, string PageTitle, string? PortuguesePageTitle = null);
public sealed record NarrativeSourceIndex(NarrativeSourceBook Book, IReadOnlyList<NarrativeChapter> Chapters,
    string RevisionId, string SourceUrl, IReadOnlyList<string> Diagnostics, string PreferredLanguage = "pt");
public sealed record CollectedNarrativeSource(int Order, string Title, string Text, GenerationSourceProvenance Provenance);
public sealed record GenerationSourceProvenance(string UnitSlug, string Provider, string Url, string RevisionId,
    string PageId, string Language, string License, string Attribution, DateTimeOffset RetrievedAt,
    string ResponseHash, string SourceHash);

public interface IBookNarrativeSourceProvider
{
    Task<NarrativeSourceIndex> GetIndexAsync(string bookKey, CancellationToken cancellationToken, string? preferredLanguage = null);
    Task<CollectedNarrativeSource?> GetChapterAsync(NarrativeSourceIndex index, NarrativeChapter chapter,
        CancellationToken cancellationToken);
}

public sealed record SourcePreparationUnit(int Order, string Title, string Status, int SummaryBytes,
    string? SourceUrl, string? RevisionId, string? SourceHash, string? SummaryPreview, string? SourceLanguage = null);
public sealed record SourcePreparationStatus(Guid PreparationId, Guid BookId, int Revision, string State,
    string SourceBookTitle, string Language, string SourceUrl, string Attribution, string License,
    int CompletedUnits, int TotalUnits, IReadOnlyList<SourcePreparationUnit> Units,
    IReadOnlyList<string> Diagnostics, DateTimeOffset ExpiresAt, Guid? GenerationRunId,
    string TargetLanguage = "pt-BR", bool LanguageInferred = true, string LanguageSource = "Default");
public sealed record SourcePreparationGenerateRequest(Guid OperationId, int ExpectedRevision,
    bool EditionConfirmed, bool IndexComplete);
public sealed record SourcePreparationAvailability(bool Supported, string? Reason,
    IReadOnlyList<string> SupportedTitles, string TargetLanguage = "pt-BR", bool LanguageInferred = true,
    string LanguageSource = "Default");
