namespace Mnemora.Domain;

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public required string Author { get; set; }
    public string? Description { get; set; }
    public string? Isbn10 { get; set; }
    public string? Isbn13 { get; set; }
    public string? CoverUrl { get; set; }
    public string? Publisher { get; set; }
    public DateOnly? PublishedDate { get; set; }
    public string? Language { get; set; }
    public string? ExternalProvider { get; set; }
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ReadingUnitType { Part, Chapter, Section, Subsection }

public sealed class ReadingUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public Guid? ParentUnitId { get; set; }
    public required string Title { get; set; }
    public required string SafeLabel { get; set; }
    public required string Slug { get; set; }
    public ReadingUnitType Type { get; set; }
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum UserBookStatus { WantToRead, Reading, Paused, Finished }

public sealed class UserBook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public UserBookStatus Status { get; set; } = UserBookStatus.WantToRead;
    public Guid? CurrentReadingUnitId { get; set; }
    public int? CurrentPage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? LastReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
