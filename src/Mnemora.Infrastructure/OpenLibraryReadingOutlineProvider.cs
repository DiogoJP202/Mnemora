using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class OpenLibraryReadingOutlineProvider(HttpClient client)
    : IReadingOutlineProvider
{
    private const int MaxItems = 200;

    public async Task<ExternalReadingOutline?> GetAsync(
        string provider, string externalId, CancellationToken cancellationToken)
    {
        if (!string.Equals(provider, BookMetadataProviders.OpenLibrary,
                StringComparison.OrdinalIgnoreCase))
            return null;
        var editionId = NormalizeEditionId(externalId);
        if (editionId is null) return null;

        using var response = await client.GetAsync(
            $"books/{editionId}.json", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        var items = Parse(document);
        return items.Count == 0 ? null : new ExternalReadingOutline(
            BookMetadataProviders.OpenLibrary,
            editionId,
            $"https://openlibrary.org/books/{editionId}",
            "Open Library",
            items);
    }

    internal static IReadOnlyList<ExternalReadingOutlineItem> Parse(
        JsonDocument? document)
    {
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("table_of_contents", out var table)
            || table.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<ExternalReadingOutlineItem>();
        foreach (var item in table.EnumerateArray())
        {
            string? title = null;
            var level = 1;
            int? page = null;
            if (item.ValueKind == JsonValueKind.String)
            {
                title = item.GetString();
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                title = StringValue(item, "title");
                level = PositiveInt(item, "level") ?? 1;
                page = PositiveInt(item, "pagenum") ?? PositiveInt(item, "page");
            }
            title = Normalize(title);
            if (title is null) continue;
            if (result.Count == MaxItems)
                throw new ReadingOutlineLimitExceededException(MaxItems);
            result.Add(new ExternalReadingOutlineItem(
                title.Length <= 300 ? title : title[..300],
                Math.Clamp(level, 1, 4),
                page));
        }
        return result;
    }

    private static string? StringValue(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? PositiveInt(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var numeric) && numeric > 0)
            return numeric;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out numeric) && numeric > 0)
            return numeric;
        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeEditionId(string? value)
    {
        var id = value?.Trim();
        if (id?.StartsWith("/books/", StringComparison.OrdinalIgnoreCase) == true)
            id = id[7..];
        id = id?.ToUpperInvariant();
        return id is { Length: >= 4 and <= 40 }
            && id.StartsWith("OL", StringComparison.Ordinal)
            && id.EndsWith('M')
            && id[2..^1].All(char.IsDigit)
                ? id
                : null;
    }
}
