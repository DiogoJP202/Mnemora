using Microsoft.Extensions.Logging;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class CompositeBookMetadataProvider(
    IEnumerable<IBookMetadataSource> sources,
    ILogger<CompositeBookMetadataProvider> logger) : IBookMetadataProvider
{
    private readonly IBookMetadataSource[] sources = sources.ToArray();

    public bool IsConfigured => sources.Any(source => source.IsConfigured);

    public async Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        var configured = sources.Where(source => source.IsConfigured).ToArray();
        if (configured.Length == 0) return [];

        var attempts = await Task.WhenAll(configured.Select(source =>
            SearchSourceAsync(source, query, cancellationToken)));
        var successful = attempts.Where(attempt => attempt.Error is null).ToArray();
        if (successful.Length == 0)
            throw new HttpRequestException(
                "Nenhum provedor de metadados respondeu à busca.",
                new AggregateException(attempts.Select(attempt => attempt.Error!)));

        var results = new List<ExternalBookMetadata>();
        var externalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bibliographicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attempt in successful)
        foreach (var item in attempt.Results)
        {
            var externalKey = $"{item.Provider}\u001f{item.ExternalId}";
            var bibliographicKey = $"{item.Title.Trim()}\u001f{item.Author.Trim()}";
            if (string.IsNullOrWhiteSpace(item.Provider)
                || string.IsNullOrWhiteSpace(item.ExternalId)
                || string.IsNullOrWhiteSpace(item.Title)
                || !externalKeys.Add(externalKey)
                || !bibliographicKeys.Add(bibliographicKey))
                continue;
            results.Add(item);
        }
        return results;
    }

    public Task<ExternalBookMetadata?> GetAsync(
        string provider, string externalId, CancellationToken cancellationToken)
    {
        var source = sources.FirstOrDefault(candidate => candidate.IsConfigured
            && string.Equals(candidate.Provider, provider,
                StringComparison.OrdinalIgnoreCase));
        return source is null
            ? Task.FromResult<ExternalBookMetadata?>(null)
            : source.GetAsync(externalId, cancellationToken);
    }

    private async Task<SearchAttempt> SearchSourceAsync(
        IBookMetadataSource source, string query, CancellationToken cancellationToken)
    {
        try
        {
            return new SearchAttempt(
                await source.SearchAsync(query, cancellationToken), null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Falha ao buscar metadados no provedor {Provider}.", source.Provider);
            return new SearchAttempt([], exception);
        }
    }

    private sealed record SearchAttempt(
        IReadOnlyList<ExternalBookMetadata> Results, Exception? Error);
}
