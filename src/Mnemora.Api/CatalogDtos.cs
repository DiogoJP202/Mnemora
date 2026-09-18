using Mnemora.Domain;

namespace Mnemora.Api;

public sealed record BookDto(
    Guid Id, string Title, string? Subtitle, string Author,
    string? Description, string? CoverUrl, string? Isbn10, string? Isbn13,
    string? Publisher, DateOnly? PublishedDate, string? Language,
    bool MemoryPackAvailable = false);
public sealed record BookSearchDto(
    Guid? Id, string? ExternalProvider, string? ExternalId, string Source,
    string Title, string? Subtitle, string Author, string? CoverUrl,
    string? Isbn10, string? Isbn13, string? Publisher,
    DateOnly? PublishedDate, string? Language, int? PageCount,
    IReadOnlyList<string>? Categories, string? Edition,
    bool MemoryPackAvailable);
public sealed record LibraryBookDto(
    BookDto Book, string Status, Guid? CurrentReadingUnitId,
    int? CurrentPage, int ProgressPercentage);
public sealed record ReadingUnitDto(
    Guid Id, Guid? ParentUnitId, string Label, string Title,
    string Type, int OrderIndex, bool IsKnown, bool IsCurrent);

public static class CatalogDto
{
    public static BookDto From(Book book, bool memoryPackAvailable = false) =>
        new(book.Id, book.Title, book.Subtitle, book.Author,
            book.Description, book.CoverUrl, book.Isbn10, book.Isbn13,
            book.Publisher, book.PublishedDate, book.Language,
            memoryPackAvailable);
}
