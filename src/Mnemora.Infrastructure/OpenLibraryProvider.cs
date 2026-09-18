using System.Net.Http.Json;
using System.Text.Json;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class OpenLibraryProvider(HttpClient client) : IBookMetadataSource
{
    private const int MaxResults = 10;
    private const string SearchFields =
        "key,title,author_name,cover_i,first_publish_year,language";

    public string Provider => BookMetadataProviders.OpenLibrary;
    public bool IsConfigured => true;

    public async Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var path = $"search.json?q={Uri.EscapeDataString(query.Trim())}"
            + $"&fields={Uri.EscapeDataString(SearchFields)}&lang=pt&limit={MaxResults}";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        return ParseResults(document);
    }

    public async Task<ExternalBookMetadata?> GetAsync(
        string externalId, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeWorkId(externalId);
        if (normalizedId is null) return null;

        var query = $"key:/works/{normalizedId}";
        var path = $"search.json?q={Uri.EscapeDataString(query)}"
            + $"&fields={Uri.EscapeDataString(SearchFields)}&lang=pt&limit=1";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        return ParseResults(document).FirstOrDefault(result =>
            string.Equals(result.ExternalId, normalizedId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ExternalBookMetadata> ParseResults(JsonDocument? document)
    {
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("docs", out var docs)
            || docs.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<ExternalBookMetadata>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in docs.EnumerateArray())
        {
            var metadata = Parse(item);
            if (metadata is not null && ids.Add(metadata.ExternalId))
                results.Add(metadata);
        }
        return results;
    }

    private static ExternalBookMetadata? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("key", out var key)
            || key.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("title", out var titleElement)
            || titleElement.ValueKind != JsonValueKind.String)
            return null;

        var externalId = NormalizeWorkId(key.GetString());
        var title = titleElement.GetString()?.Trim();
        if (externalId is null || string.IsNullOrWhiteSpace(title)) return null;

        var authors = item.TryGetProperty("author_name", out var authorNames)
            && authorNames.ValueKind == JsonValueKind.Array
            ? authorNames.EnumerateArray()
                .Where(author => author.ValueKind == JsonValueKind.String)
                .Select(author => author.GetString()!.Trim())
                .Where(author => !string.IsNullOrWhiteSpace(author))
                .Select(author => author!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var author = authors.Length == 0
            ? "Autor não informado"
            : string.Join(", ", authors);

        string? coverUrl = null;
        if (item.TryGetProperty("cover_i", out var cover)
            && cover.TryGetInt64(out var coverId) && coverId > 0)
            coverUrl = $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg?default=false";

        DateOnly? publishedDate = null;
        if (item.TryGetProperty("first_publish_year", out var yearElement)
            && yearElement.TryGetInt32(out var year) && year is >= 1 and <= 9999)
            publishedDate = new DateOnly(year, 1, 1);

        string? language = null;
        if (item.TryGetProperty("language", out var languages)
            && languages.ValueKind == JsonValueKind.Array)
            language = languages.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new ExternalBookMetadata(
            BookMetadataProviders.OpenLibrary,
            externalId,
            Limit(title, 300),
            Limit(author, 300),
            coverUrl,
            null,
            null,
            null,
            publishedDate,
            language);
    }

    private static string? NormalizeWorkId(string? value)
    {
        var id = value?.Trim();
        if (id?.StartsWith("/works/", StringComparison.OrdinalIgnoreCase) == true)
            id = id[7..];
        id = id?.ToUpperInvariant();
        if (id is null || id.Length is < 4 or > 40
            || !id.StartsWith("OL", StringComparison.Ordinal)
            || !id.EndsWith('W')
            || !id[2..^1].All(char.IsDigit))
            return null;
        return id;
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
