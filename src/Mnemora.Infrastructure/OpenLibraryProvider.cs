using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class OpenLibraryProvider(HttpClient client) : IBookMetadataSource
{
    private const int MaxResults = 10;
    private const string SearchFields =
        "key,title,subtitle,author_name,cover_i,first_publish_year,language,isbn,"
        + "publisher,number_of_pages_median,subject,editions,editions.key,"
        + "editions.title,editions.subtitle,editions.isbn,editions.publisher,"
        + "editions.publish_date,editions.language,editions.number_of_pages,"
        + "editions.cover_i,editions.edition_name,editions.physical_format";

    public string Provider => BookMetadataProviders.OpenLibrary;
    public bool IsConfigured => true;

    public async Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var normalizedIsbn = BookIsbn.Normalize(query);
        var search = normalizedIsbn is { IsValid: true, HasValue: true }
            ? $"isbn={Uri.EscapeDataString(normalizedIsbn.Canonical!)}"
            : $"q={Uri.EscapeDataString(query.Trim())}";
        var path = $"search.json?{search}"
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
        var normalizedId = NormalizeBookId(externalId);
        if (normalizedId is null) return null;

        var query = normalizedId.EndsWith('W')
            ? $"key:/works/{normalizedId}"
            : $"edition_key:{normalizedId}";
        var path = $"search.json?q={Uri.EscapeDataString(query)}"
            + $"&fields={Uri.EscapeDataString(SearchFields)}&lang=pt&limit=1";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        return ParseExactResult(document, normalizedId);
    }

    private static IReadOnlyList<ExternalBookMetadata> ParseResults(JsonDocument? document)
    {
        if (!TryGetDocuments(document, out var docs)) return [];

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

    private static ExternalBookMetadata? ParseExactResult(
        JsonDocument? document, string normalizedId)
    {
        if (!TryGetDocuments(document, out var docs)) return null;

        foreach (var item in docs.EnumerateArray())
        {
            var workId = item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String
                ? NormalizeWorkId(key.GetString())
                : null;
            var metadata = Parse(item);
            if (metadata is null) continue;

            if (normalizedId.EndsWith('W')
                && string.Equals(workId, normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                return metadata with { ExternalId = normalizedId };
            if (normalizedId.EndsWith('M')
                && string.Equals(metadata.ExternalId, normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                return metadata;
        }
        return null;
    }

    private static bool TryGetDocuments(
        JsonDocument? document, out JsonElement documents)
    {
        if (document is not null
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("docs", out documents)
            && documents.ValueKind == JsonValueKind.Array)
            return true;
        documents = default;
        return false;
    }

    private static ExternalBookMetadata? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("key", out var key)
            || key.ValueKind != JsonValueKind.String)
            return null;

        var workId = NormalizeWorkId(key.GetString());
        if (workId is null) return null;

        var edition = BestEdition(item);
        var externalId = edition is { } editionValue
            && editionValue.TryGetProperty("key", out var editionKey)
            ? NormalizeEditionId(editionKey.GetString()) ?? workId
            : workId;
        var title = FirstString(edition, "title", 300)
            ?? FirstString(item, "title", 300);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var authors = StringArray(item, "author_name", 20, 300);
        var author = authors is { Count: > 0 }
            ? string.Join(", ", authors)
            : "Autor não informado";

        var coverId = PositiveInt64(edition, "cover_i")
            ?? PositiveInt64(item, "cover_i");
        var coverUrl = coverId is not null
            ? $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg?default=false"
            : null;

        var (isbn10, isbn13) = ParseIsbns(edition ?? item);
        var publisher = FirstString(edition, "publisher", 300);
        if (edition is null) publisher ??= FirstString(item, "publisher", 300);

        var publishedDate = ParseFirstDate(edition, "publish_date");
        if (edition is null) publishedDate ??= ParseYear(item, "first_publish_year");

        var language = FirstString(edition, "language", 30);
        if (edition is null) language ??= FirstString(item, "language", 30);

        var pageCount = PositiveInt32(edition, "number_of_pages");
        if (edition is null)
            pageCount ??= PositiveInt32(item, "number_of_pages_median");

        var categories = StringArray(item, "subject", 12, 100);
        var editionName = FirstString(edition, "edition_name", 100)
            ?? FirstString(edition, "physical_format", 100);

        return new ExternalBookMetadata(
            BookMetadataProviders.OpenLibrary,
            externalId,
            title,
            Limit(author, 300),
            coverUrl,
            isbn10,
            isbn13,
            publisher,
            publishedDate,
            language,
            FirstString(edition, "subtitle", 300)
                ?? FirstString(item, "subtitle", 300),
            pageCount,
            categories,
            editionName,
            workId);
    }

    private static JsonElement? BestEdition(JsonElement item)
    {
        if (!item.TryGetProperty("editions", out var editions)
            || editions.ValueKind != JsonValueKind.Object
            || !editions.TryGetProperty("docs", out var documents)
            || documents.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var edition in documents.EnumerateArray())
            if (edition.ValueKind == JsonValueKind.Object
                && edition.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String
                && NormalizeEditionId(key.GetString()) is not null)
                return edition;
        return null;
    }

    private static (string? Isbn10, string? Isbn13) ParseIsbns(JsonElement source)
    {
        if (!source.TryGetProperty("isbn", out var values)) return (null, null);

        IEnumerable<string?> candidates = values.ValueKind switch
        {
            JsonValueKind.Array => values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()),
            JsonValueKind.String => [values.GetString()],
            _ => []
        };
        string? isbn10 = null, isbn13 = null;
        foreach (var candidate in candidates)
        {
            var normalized = BookIsbn.Normalize(candidate);
            if (!normalized.IsValid || !normalized.HasValue) continue;
            isbn10 ??= normalized.Isbn10;
            isbn13 ??= normalized.Isbn13;
            if (isbn10 is not null && isbn13 is not null) break;
        }
        return (isbn10, isbn13);
    }

    private static IReadOnlyList<string>? StringArray(
        JsonElement source, string property, int maxItems, int maxLength)
    {
        if (!source.TryGetProperty(property, out var values)) return null;
        IEnumerable<string?> candidates = values.ValueKind switch
        {
            JsonValueKind.Array => values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()),
            JsonValueKind.String => [values.GetString()],
            _ => []
        };
        var result = candidates
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Limit(value!, maxLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToArray();
        return result.Length == 0 ? null : result;
    }

    private static string? FirstString(
        JsonElement? source, string property, int maxLength)
    {
        if (source is not { } value
            || !value.TryGetProperty(property, out var element))
            return null;
        string? result = null;
        if (element.ValueKind == JsonValueKind.String)
            result = element.GetString();
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var candidate in element.EnumerateArray())
                if (candidate.ValueKind == JsonValueKind.String)
                {
                    var candidateValue = candidate.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(candidateValue))
                    {
                        result = candidateValue;
                        break;
                    }
                }
        result = result?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : Limit(result, maxLength);
    }

    private static DateOnly? ParseFirstDate(JsonElement? source, string property)
    {
        var value = FirstString(source, property, 80);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length == 4 && int.TryParse(value, out var year)
            && year is >= 1 and <= 9999)
            return new DateOnly(year, 1, 1);
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
    }

    private static DateOnly? ParseYear(JsonElement source, string property) =>
        source.TryGetProperty(property, out var element)
        && element.TryGetInt32(out var year) && year is >= 1 and <= 9999
            ? new DateOnly(year, 1, 1)
            : null;

    private static int? PositiveInt32(JsonElement? source, string property) =>
        source is { } value && value.TryGetProperty(property, out var element)
        && element.TryGetInt32(out var result) && result > 0
            ? result
            : null;

    private static long? PositiveInt64(JsonElement? source, string property) =>
        source is { } value && value.TryGetProperty(property, out var element)
        && element.TryGetInt64(out var result) && result > 0
            ? result
            : null;

    private static string? NormalizeBookId(string? value) =>
        NormalizeOpenLibraryId(value, 'W', 'M');

    private static string? NormalizeWorkId(string? value) =>
        NormalizeOpenLibraryId(value, 'W');

    private static string? NormalizeEditionId(string? value) =>
        NormalizeOpenLibraryId(value, 'M');

    private static string? NormalizeOpenLibraryId(
        string? value, params char[] allowedSuffixes)
    {
        var id = value?.Trim();
        if (id?.StartsWith("/works/", StringComparison.OrdinalIgnoreCase) == true)
            id = id[7..];
        else if (id?.StartsWith("/books/", StringComparison.OrdinalIgnoreCase) == true)
            id = id[7..];
        id = id?.ToUpperInvariant();
        if (id is null || id.Length is < 4 or > 40
            || !id.StartsWith("OL", StringComparison.Ordinal)
            || !allowedSuffixes.Contains(id[^1])
            || !id[2..^1].All(char.IsDigit))
            return null;
        return id;
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
