namespace Mnemora.Domain;

public enum LoreEntityType
{
    Character, Faction, Location, Creature, Item, Event, Organization, Concept, Other
}

public sealed class LoreEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public LoreEntityType Type { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    // This summary and image must be safe at FirstKnownAtUnitId.
    public string? ShortDescription { get; set; }
    public string? ImageUrl { get; set; }
    public Guid FirstKnownAtUnitId { get; set; }
    public int Importance { get; set; } = 1;
    public int? ChronologyIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EntityAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public required string Alias { get; set; }
    public Guid RevealAtUnitId { get; set; }
}

public enum LoreFactType { Description, Title, Background, Event, RelationshipContext, Other }

public sealed class LoreFact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public required string Content { get; set; }
    public string? MemoryHint { get; set; }
    public LoreFactType Type { get; set; }
    public int Importance { get; set; } = 1;
    public Guid RevealAtUnitId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LoreRelation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public required string RelationType { get; set; }
    public string? Label { get; set; }
    public Guid RevealAtUnitId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
