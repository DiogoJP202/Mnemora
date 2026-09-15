namespace Mnemora.Application;

public sealed record ExternalBookMetadata(
    string ExternalId, string Title, string Author, string? CoverUrl,
    string? Isbn10, string? Isbn13, string? Publisher, DateOnly? PublishedDate,
    string? Language);

public interface IBookMetadataProvider
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken);
    Task<ExternalBookMetadata?> GetAsync(
        string externalId, CancellationToken cancellationToken);
}
