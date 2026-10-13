using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

/// <summary>Read-only gate shared by generation start, each step and result delivery.</summary>
public sealed class MemoryPackGenerationTarget(MnemoraDbContext db, MemoryPackTransfer transfer)
{
    public async Task<MemoryPackBook> ReadAsync(Guid bookId, MemoryPackBook? expected = null,
        CancellationToken cancellationToken = default)
    {
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == bookId && x.CatalogKind != BookCatalogKind.Private, cancellationToken);
        if (book is null)
            throw new GenerationException("book_not_found", "Livro não encontrado.", 404);
        if (book.ReadingOutlineStatus == EditorialContentStatus.Published
            || book.LoreStatus == EditorialContentStatus.Published
            || await db.ReadingUnits.AnyAsync(x => x.BookId == bookId, cancellationToken)
            || await db.LoreEntities.AnyAsync(x => x.BookId == bookId, cancellationToken)
            || await db.LoreRelations.AnyAsync(x => x.BookId == bookId, cancellationToken))
            throw new GenerationException("book_has_content",
                "Este livro já possui conteúdo editorial ou publicação. Use a exportação e os editores existentes; a geração não substitui conteúdo.", 409);

        // Keep the exact bibliographic normalization owned by the existing exporter.
        var document = await transfer.ExportAsync(bookId, cancellationToken);
        if (document is null)
            throw new GenerationException("book_not_found", "Livro não encontrado.", 404);
        if (expected is not null && document.Book != expected)
            throw new GenerationException("edition_changed",
                "A edição foi alterada durante a geração. Inicie uma nova sessão após conferir os metadados.", 409);
        return document.Book;
    }
}
