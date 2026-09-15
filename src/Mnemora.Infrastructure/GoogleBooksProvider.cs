using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class GoogleBooksProvider(
    HttpClient client, IConfiguration configuration) : IBookMetadataProvider
{
    private readonly string? key = configuration["GOOGLE_BOOKS_API_KEY"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(key);

    public async Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];
        using var response = await client.GetAsync(
            $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=10&key={Uri.EscapeDataString(key!)}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("items", out var items))
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
        if (!response.IsSuccessStatusCode) return null;
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            cancellationToken: cancellationToken);
        return document is null ? null : Parse(document.RootElement);
    }

    private static ExternalBookMetadata? Parse(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var id)
            || !item.TryGetProperty("volumeInfo", out var info)
            || !info.TryGetProperty("title", out var title))
            return null;
        var author = info.TryGetProperty("authors", out var authors)
            && authors.ValueKind == JsonValueKind.Array
            ? string.Join(", ", authors.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null))
            : "Autor não informado";
        string? cover = null;
        if (info.TryGetProperty("imageLinks", out var images))
        {
            if (images.TryGetProperty("thumbnail", out var thumb))
                cover = thumb.GetString()?.Replace("http://", "https://");
        }
        string? isbn10 = null, isbn13 = null;
        if (info.TryGetProperty("industryIdentifiers", out var identifiers)
            && identifiers.ValueKind == JsonValueKind.Array)
            foreach (var identifier in identifiers.EnumerateArray())
            {
                if (!identifier.TryGetProperty("type", out var type)
                    || !identifier.TryGetProperty("identifier", out var value)) continue;
                if (type.GetString() == "ISBN_10") isbn10 = value.GetString();
                if (type.GetString() == "ISBN_13") isbn13 = value.GetString();
            }
        DateOnly? publishedDate = null;
        if (info.TryGetProperty("publishedDate", out var date)
            && DateOnly.TryParse(date.GetString(), out var parsed))
            publishedDate = parsed;
        return new ExternalBookMetadata(
            id.GetString() ?? "", title.GetString() ?? "", author, cover,
            isbn10, isbn13,
            info.TryGetProperty("publisher", out var publisher) ? publisher.GetString() : null,
            publishedDate,
            info.TryGetProperty("language", out var language) ? language.GetString() : null);
    }
}
