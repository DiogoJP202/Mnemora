using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

// Imports bibliographic data only. Library membership and editorial changes belong
// to their own endpoints, so an Admin import never creates a personal association.
public sealed class ExternalBookImporter(MnemoraDbContext db, IBookMetadataProvider provider)
{
    public async Task<Book> ImportAsync(string externalProvider, string externalId,
        CancellationToken cancellationToken = default)
    {
        if (!provider.IsConfigured)
            throw new BookImportException("Busca externa não está configurada.", 503);
        if (string.IsNullOrWhiteSpace(externalProvider) || externalProvider.Length > 80
            || string.IsNullOrWhiteSpace(externalId) || externalId.Length > 200)
            throw new BookImportException("Identificador externo inválido.", 400);

        var metadata = await provider.GetAsync(externalProvider.Trim(), externalId.Trim(), cancellationToken);
        if (metadata is null) throw new BookImportException("Edição não encontrada.", 404);
        var providerName = metadata.Provider?.Trim();
        var editionId = metadata.ExternalId?.Trim();
        var workId = metadata.WorkId?.Trim();
        var title = Normalize(metadata.Title);
        var author = Normalize(metadata.Author) ?? "Autor não informado";
        if (string.IsNullOrWhiteSpace(providerName) || providerName.Length > 80
            || string.IsNullOrWhiteSpace(editionId) || editionId.Length > 200
            || workId?.Length > 200 || title is null || title.Length > 300 || author.Length > 300)
            throw new BookImportException("O catálogo externo retornou dados inválidos.", 503);

        var isbn = BookIsbn.Normalize(metadata.Isbn13);
        if (!isbn.IsValid || !isbn.HasValue) isbn = BookIsbn.Normalize(metadata.Isbn10);
        if (!isbn.IsValid || !isbn.HasValue) isbn = new(true, null, null);
        var book = await FindAsync(providerName, editionId, workId, cancellationToken);
        if (book is null)
        {
            book = new Book
            {
                Title = title, Author = author, Subtitle = Optional(metadata.Subtitle, 300),
                CoverUrl = Optional(metadata.CoverUrl, 2_000), Isbn10 = isbn.Isbn10, Isbn13 = isbn.Isbn13,
                Publisher = Optional(metadata.Publisher, 300), PublishedDate = metadata.PublishedDate,
                Language = Optional(metadata.Language, 35), ExternalProvider = providerName,
                ExternalId = editionId, CatalogKind = BookCatalogKind.Imported,
                ReadingOutlineStatus = EditorialContentStatus.None, LoreStatus = EditorialContentStatus.None,
                Description = null
            };
            db.Books.Add(book);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                book = await FindAsync(providerName, editionId, workId, cancellationToken);
                if (book is null) throw;
            }
        }
        else if (Enrich(book, metadata, isbn)) await db.SaveChangesAsync(cancellationToken);
        return book;
    }

    private async Task<Book?> FindAsync(string provider, string editionId, string? workId,
        CancellationToken cancellationToken)
    {
        var book = await db.Books.FirstOrDefaultAsync(x =>
            x.ExternalProvider == provider && x.ExternalId == editionId, cancellationToken);
        if (book is null && !string.IsNullOrWhiteSpace(workId))
            book = await db.Books.FirstOrDefaultAsync(x =>
                x.ExternalProvider == provider && x.ExternalId == workId, cancellationToken);
        if (book?.CatalogKind == BookCatalogKind.Private)
            throw new BookImportException("Edição não encontrada.", 404);
        return book;
    }

    private static bool Enrich(Book book, ExternalBookMetadata metadata, BookIsbnNormalization isbn)
    {
        if (book.CatalogKind != BookCatalogKind.Imported) return false;
        var changed = false;
        changed |= SetMissing(book.Subtitle, value => book.Subtitle = value, Optional(metadata.Subtitle, 300));
        changed |= SetMissing(book.CoverUrl, value => book.CoverUrl = value, Optional(metadata.CoverUrl, 2_000));
        changed |= SetMissing(book.Isbn10, value => book.Isbn10 = value, isbn.Isbn10);
        changed |= SetMissing(book.Isbn13, value => book.Isbn13 = value, isbn.Isbn13);
        changed |= SetMissing(book.Publisher, value => book.Publisher = value, Optional(metadata.Publisher, 300));
        changed |= SetMissing(book.Language, value => book.Language = value, Optional(metadata.Language, 35));
        if (book.PublishedDate is null && metadata.PublishedDate is not null)
        {
            book.PublishedDate = metadata.PublishedDate;
            changed = true;
        }
        if (changed) book.UpdatedAt = DateTime.UtcNow;
        return changed;
    }

    private static bool SetMissing(string? current, Action<string> setter, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(candidate)) return false;
        setter(candidate);
        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Optional(string? value, int max)
    {
        var text = Normalize(value);
        return text?.Length > max ? text[..max] : text;
    }
}

public sealed class BookImportException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
