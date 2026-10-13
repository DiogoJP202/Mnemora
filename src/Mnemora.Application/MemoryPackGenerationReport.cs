namespace Mnemora.Application;

// Administrative evidence travels beside the document. These fields never enter JSON v1.
public sealed record GenerationReportEvidence(
    string UnitSlug,
    string SourceHash,
    string ParagraphId,
    string Quote,
    string Justification,
    string ItemId,
    string JsonPath);

public sealed record GenerationAssemblyResult(
    MemoryPackDocument Document,
    IReadOnlyList<GenerationReportEvidence> Evidence,
    IReadOnlyList<string> Diagnostics);
