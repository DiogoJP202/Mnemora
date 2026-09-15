using Mnemora.Domain;

namespace Mnemora.Api;

public sealed record BookDto(
    Guid Id, string Title, string Author, string? Description, string? CoverUrl);
public sealed record BookSearchDto(
    Guid? Id, string? ExternalId, string Source,
    string Title, string Author, string? CoverUrl);
public sealed record LibraryBookDto(
    BookDto Book, string Status, Guid? CurrentReadingUnitId,
    int? CurrentPage, int ProgressPercentage);
public sealed record ReadingUnitDto(
    Guid Id, Guid? ParentUnitId, string Label, string Title,
    string Type, int OrderIndex, bool IsKnown, bool IsCurrent);

public static class CatalogDto
{
    public static BookDto From(Book book) =>
        new(book.Id, book.Title, book.Author, book.Description, book.CoverUrl);
}
