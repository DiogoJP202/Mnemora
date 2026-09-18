using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class GoogleBooksProvider(
    HttpClient client, IConfiguration configuration) : IBookMetadataSource
{
    private readonly string? key = configuration["GOOGLE_BOOKS_API_KEY"];
    public string Provider => BookMetadataProviders.GoogleBooks;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(key);

    public async Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];

        var normalizedIsbn = BookIsbn.Normalize(query);
        var providerQuery = normalizedIsbn is { IsValid: true, HasValue: true }
            ? $"isbn:{normalizedIsbn.Canonical}"
            : query.Trim();
        using var response = await client.GetAsync(
            $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(providerQuery)}&maxResults=10&key={Uri.EscapeDataString(key!)}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return [];
        return items.EnumerateArray().Select(Parse).Where(x => x is not null)
            .Cast<ExternalBookMetadata>().ToList();
    }

    public async Task<ExternalBookMetadata?> GetAsync(
        string externalId, CancellationToken cancellationToken)
    {
        if (!IsConfigured || externalId.Length > 100
            || externalId.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and '_'))
            return null;
        using var response = await client.GetAsync(
            $"https://www.googleapis.com/books/v1/volumes/{Uri.EscapeDataString(externalId)}?key={Uri.EscapeDataString(key!)}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        return document is null ? null : Parse(document.RootElement);
    }

    private static ExternalBookMetadata? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString())
            || !item.TryGetProperty("volumeInfo", out var info)
            || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("title", out var title)
            || title.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(title.GetString()))
            return null;
        var authorNames = info.TryGetProperty("authors", out var authors)
            && authors.ValueKind == JsonValueKind.Array
            ? authors.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var author = authorNames.Length == 0
            ? "Autor não informado"
            : string.Join(", ", authorNames);
        var cover = info.TryGetProperty("imageLinks", out var images)
            && images.ValueKind == JsonValueKind.Object
            ? BestCover(images)
            : null;
        string? isbn10 = null, isbn13 = null;
        if (info.TryGetProperty("industryIdentifiers", out var identifiers)
            && identifiers.ValueKind == JsonValueKind.Array)
            foreach (var identifier in identifiers.EnumerateArray())
            {
                if (identifier.ValueKind != JsonValueKind.Object
                    || !identifier.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || !identifier.TryGetProperty("identifier", out var value)
                    || value.ValueKind != JsonValueKind.String)
                    continue;
                var normalized = BookIsbn.Normalize(value.GetString());
                if (!normalized.IsValid || !normalized.HasValue) continue;
                if (type.GetString() == "ISBN_10")
                {
                    isbn10 ??= normalized.Isbn10;
                    isbn13 ??= normalized.Isbn13;
                }
                if (type.GetString() == "ISBN_13")
                {
                    isbn13 ??= normalized.Isbn13;
                    isbn10 ??= normalized.Isbn10;
                }
            }

        var categories = StringArray(info, "categories", 12);
        return new ExternalBookMetadata(
            BookMetadataProviders.GoogleBooks,
            id.GetString() ?? "", Limit(title.GetString() ?? "", 300),
            Limit(author, 300), cover,
            isbn10, isbn13,
            String(info, "publisher", 300),
            ParseDate(String(info, "publishedDate", 80)),
            String(info, "language", 30),
            String(info, "subtitle", 300),
            info.TryGetProperty("pageCount", out var pages)
                && pages.TryGetInt32(out var pageCount) && pageCount > 0
                ? pageCount
                : null,
            categories);
    }

    private static string? BestCover(JsonElement images)
    {
        foreach (var name in new[]
                 {
                     "extraLarge", "large", "medium", "small", "thumbnail",
                     "smallThumbnail"
                 })
        {
            if (!images.TryGetProperty(name, out var image)
                || image.ValueKind != JsonValueKind.String)
                continue;

            var value = image.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value.StartsWith("//", StringComparison.Ordinal))
                value = $"https:{value}";
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                value = $"https://{value[7..]}";
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
                return uri.AbsoluteUri;
        }
        return null;
    }

    private static string? String(JsonElement source, string property, int max)
    {
        if (!source.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
            return null;
        var value = element.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : Limit(value, max);
    }

    private static IReadOnlyList<string>? StringArray(
        JsonElement source, string property, int maxItems)
    {
        if (!source.TryGetProperty(property, out var values)
            || values.ValueKind != JsonValueKind.Array)
            return null;
        var result = values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Limit(value!, 100))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToArray();
        return result.Length == 0 ? null : result;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length == 4 && int.TryParse(text, out var year)
            && year is >= 1 and <= 9999)
            return new DateOnly(year, 1, 1);
        if (text.Length == 7
            && DateOnly.TryParseExact($"{text}-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            return month;
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
