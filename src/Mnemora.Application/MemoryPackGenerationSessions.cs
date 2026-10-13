using System.Text.Json.Serialization;

namespace Mnemora.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationNextRequest(Guid OperationId, int ExpectedRevision);

public sealed record GenerationUnitCoverage(string UnitSlug, string Title, string Status, string SourceHash);

public sealed record GenerationSessionStatus(Guid RunId, Guid BookId, int Revision, string State,
    int CompletedUnits, int TotalUnits, int Calls, int Tokens, int EstimatedTokens,
    DateTimeOffset? NextAllowedAt, IReadOnlyList<GenerationUnitCoverage> Coverage,
    IReadOnlyList<string> Diagnostics, bool HasImportableResult);

public sealed record GenerationReport(string Provider, string Model, string PromptVersion,
    string? SourceAttribution, string? SourceUrl, bool IndexComplete, bool Partial,
    string PackHash, int Calls, int Tokens, int EstimatedTokens,
    IReadOnlyList<GenerationUnitCoverage> Units,
    IReadOnlyList<GenerationReportEvidence> Evidence, IReadOnlyList<string> Diagnostics);

public sealed record GenerationSessionResult(GenerationSessionStatus Status,
    MemoryPackDocument Document, GenerationReport Report, MemoryPackValidationResult Validation,
    string DocumentJson);
