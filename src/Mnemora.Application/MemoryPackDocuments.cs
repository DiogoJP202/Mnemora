using System.Text.Json.Serialization;

namespace Mnemora.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackDocument(
    int SchemaVersion,
    MemoryPackBook Book,
    MemoryPackSource? Source,
    IReadOnlyList<MemoryPackUnit> ReadingUnits,
    IReadOnlyList<MemoryPackEntity> Entities,
    IReadOnlyList<MemoryPackRelation> Relations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackBook(string Title, string Author,
    string? ExternalProvider = null, string? ExternalId = null,
    string? Isbn10 = null, string? Isbn13 = null, string? Language = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackSource(string? Url = null, string? Attribution = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackUnit(string Slug, string? ParentSlug, string Title,
    string SafeLabel, string Type, int OrderIndex, string? Description = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackEntity(string Slug, string Type, string Name,
    string? ShortDescription, string? ImageUrl, string FirstKnownAtUnitSlug,
    int Importance, int? ChronologyIndex,
    IReadOnlyList<MemoryPackAlias> Aliases, IReadOnlyList<MemoryPackFact> Facts);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackAlias(string Alias, string RevealAtUnitSlug);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackFact(string Content, string? MemoryHint,
    string Type, int Importance, string RevealAtUnitSlug);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MemoryPackRelation(string SourceEntitySlug,
    string TargetEntitySlug, string RelationType, string? Label,
    string RevealAtUnitSlug);

public sealed record MemoryPackCounts(int ReadingUnits, int Entities, int Aliases,
    int Facts, int Relations, int Events);
public sealed record MemoryPackIssue(string Path, string Message);
public sealed record MemoryPackValidationResult(bool IsValid, bool AlreadyImported,
    MemoryPackCounts Counts, IReadOnlyList<MemoryPackIssue> Issues);
public sealed record MemoryPackImportResult(bool AlreadyImported, MemoryPackCounts Counts);

public sealed record MemoryPackTransferOutcome(
    MemoryPackValidationResult Validation, bool Conflict = false);
