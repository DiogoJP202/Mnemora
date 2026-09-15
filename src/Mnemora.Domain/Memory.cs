namespace Mnemora.Domain;

public sealed class UserNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? ReadingUnitId { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserRecallActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public Guid EntityId { get; set; }
    public string? Query { get; set; }
    public bool? Remembered { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
