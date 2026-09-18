namespace Mnemora.Application;

public sealed record ExternalBookMetadata(
    string Provider, string ExternalId, string Title, string Author, string? CoverUrl,
    string? Isbn10, string? Isbn13, string? Publisher, DateOnly? PublishedDate,
    string? Language);

public static class BookMetadataProviders
{
    public const string GoogleBooks = "GoogleBooks";
    public const string OpenLibrary = "OpenLibrary";
}

public interface IBookMetadataProvider
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken);
    Task<ExternalBookMetadata?> GetAsync(
        string provider, string externalId, CancellationToken cancellationToken);
}

public interface IBookMetadataSource
{
    string Provider { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken);
    Task<ExternalBookMetadata?> GetAsync(
        string externalId, CancellationToken cancellationToken);
}
