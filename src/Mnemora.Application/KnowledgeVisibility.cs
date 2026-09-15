namespace Mnemora.Application;

public static class KnowledgeVisibility
{
    public static bool CanSee(int? currentOrder, int revealOrder) =>
        currentOrder is int progress && revealOrder <= progress;
}

public sealed record KnowledgeScope(Guid UserId, Guid BookId, int? CurrentOrder);

public sealed record LoreFactDto(Guid Id, string Content, string? MemoryHint, string Type);
public sealed record LoreRelationDto(
    Guid Id, Guid SourceId, string SourceName, Guid TargetId, string TargetName,
    string RelationType, string? Label);
public sealed record LoreEntitySummaryDto(
    Guid Id, Guid BookId, string Type, string Name, string? ShortDescription,
    string? ImageUrl, string? MemoryHint);
public sealed record LoreEntityDetailDto(
    LoreEntitySummaryDto Entity,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<LoreFactDto> Facts,
    IReadOnlyList<LoreRelationDto> Relations);
public sealed record TimelineEventDto(LoreEntitySummaryDto Entity, int? ChronologyIndex);
