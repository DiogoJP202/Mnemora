using Mnemora.Domain;

namespace Mnemora.Api;

internal sealed record MemoryPackAvailability(
    bool ReadingOutlineAvailable,
    bool MemoryPackAvailable,
    string Status,
    bool CanRequest);

internal static class MemoryPackAvailabilityPolicy
{
    public static MemoryPackAvailability Evaluate(
        Book book, bool hasReadingUnits, bool hasLore, bool requested = false)
    {
        var outlineAvailable = book.ReadingOutlineStatus == EditorialContentStatus.Published
            && hasReadingUnits;
        var packAvailable = outlineAvailable
            && book.LoreStatus == EditorialContentStatus.Published
            && hasLore;
        var status = packAvailable
            ? "Available"
            : IsPreparing(book, outlineAvailable, hasReadingUnits, hasLore)
                ? "Preparing"
                : requested ? "Requested" : "Unavailable";
        return new MemoryPackAvailability(
            outlineAvailable,
            packAvailable,
            status,
            book.CatalogKind != BookCatalogKind.Private && !packAvailable);
    }

    private static bool IsPreparing(
        Book book, bool outlineAvailable, bool hasReadingUnits, bool hasLore) =>
        book.ReadingOutlineStatus == EditorialContentStatus.Draft
        || book.LoreStatus == EditorialContentStatus.Draft
        || outlineAvailable
        || (book.ReadingOutlineStatus == EditorialContentStatus.Published && hasReadingUnits)
        || (book.LoreStatus == EditorialContentStatus.Published && hasLore);
}
