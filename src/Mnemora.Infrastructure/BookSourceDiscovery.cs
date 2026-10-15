using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed class BookSourceDiscovery(Func<string, HttpClient> createClient, IMemoryCache cache, TimeProvider clock)
{
    private const int MaxResponseBytes = 1_048_576;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly HashSet<string> BookClasses = ["Q7725634", "Q571", "Q8261", "Q47461344", "Q49084"];
    private static readonly Regex EditionId = new("^OL[0-9]{1,16}M$", RegexOptions.CultureInvariant);
    private static readonly Regex WorkId = new("^OL[0-9]{1,16}W$", RegexOptions.CultureInvariant);
    private static readonly Regex EntityId = new("^Q[1-9][0-9]{0,15}$", RegexOptions.CultureInvariant);

    public static string Fingerprint(SourceBookIdentity book) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(book)));

    public async Task<BookSourceDiscoveryResult> DiscoverAsync(SourceBookIdentity book, CancellationToken cancellationToken)
    {
        var key = $"book-source-discovery:v1:{Fingerprint(book)}";
        if (cache.TryGetValue<BookSourceDiscoveryResult>(key, out var cached)) return cached!;
        if (!await gate.WaitAsync(0, cancellationToken)) throw new SourceDiscoveryBusyException();
        try
        {
            // Another request may have populated the cache while this request was scheduled.
            if (cache.TryGetValue<BookSourceDiscoveryResult>(key, out cached)) return cached!;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var outlineTask = OutlineAsync(book, deadline.Token, cancellationToken);
            var referencesTask = ReferencesAsync(book, deadline.Token, cancellationToken);
            await Task.WhenAll(outlineTask, referencesTask);
            var outline = await outlineTask;
            var references = await referencesTask;
            var sources = new List<BookSourceCandidate>(outline.Sources);
            foreach (var item in references.Items)
            {
                var match = item.IsbnMatch ? "EditionIsbn"
                    : outline.WorkId is not null && item.WorkIds.Contains(outline.WorkId) ? "WorkIdentifier"
                    : "UnverifiedWork";
                sources.AddRange(item.Sources.Select(source => source with { Match = match }));
            }
            var diagnostics = outline.Diagnostics.Concat(references.Diagnostics).ToList();
            if (references.Items.Any(item => !item.IsbnMatch
                && (outline.WorkId is null || !item.WorkIds.Contains(outline.WorkId))))
                diagnostics.Add("Há referências sem identificador bibliográfico correspondente. Confira obra, autoria e tradução; semelhança de título não confirma a edição.");
            diagnostics.Add("Nenhuma fonte narrativa por capítulo foi verificada nesta etapa. Artigos gerais podem conter revelações futuras e não são enviados ao gerador.");
            var result = new BookSourceDiscoveryResult(book, clock.GetUtcNow(), outline.Status,
                outline.Items, "NotVerified", sources, diagnostics);
            cache.Set(key, result, new MemoryCacheEntryOptions { Size = 10,
                AbsoluteExpirationRelativeToNow = outline.Status == "Failed" || references.Failed
                    ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1) });
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<OutlineDiscovery> OutlineAsync(SourceBookIdentity book, CancellationToken token, CancellationToken caller)
    {
        var id = book.ExternalId;
        if (book.ExternalProvider != BookMetadataProviders.OpenLibrary || id is null || !EditionId.IsMatch(id))
            return new("NotApplicable", null, [], [], ["O sumário automático exige uma edição exata da Open Library (OL…M). Não foi usada outra edição no lugar desta."]);
        try
        {
            using var source = await ReadAsync("discovery-openlibrary", $"books/{id}.json", token);
            if (source is null) return new("Missing", null, [], [], ["A edição não foi encontrada na Open Library."]);
            var root = source.Document.RootElement;
            if (String(root, "key") != $"/books/{id}") throw new JsonException();
            var work = Array(root, "works").Select(item => String(item, "key"))
                .Where(value => value?.StartsWith("/works/", StringComparison.Ordinal) == true)
                .Select(value => value![7..])
                .FirstOrDefault(value => value is not null && WorkId.IsMatch(value));
            var isbn = BookIsbn.Normalize(book.Isbn13 ?? book.Isbn10);
            var upstreamIsbns = Array(root, "isbn_13").Concat(Array(root, "isbn_10"))
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => BookIsbn.Normalize(item.GetString())).Where(value => value.IsValid && value.HasValue)
                .Select(value => value.Canonical).ToHashSet();
            var candidate = new BookSourceCandidate("OpenLibrary", id, book.Title,
                $"https://openlibrary.org/books/{id}", "EditionMetadata", "EditionIdentifier", book.Language,
                "Open Library", source.At, source.Hash);
            if (isbn.HasValue && !upstreamIsbns.Contains(isbn.Canonical))
                return new("Conflict", work, [], [candidate], ["O ISBN do cadastro não foi confirmado na resposta desta edição. O sumário não foi aproveitado; confira a identidade antes de preparar conteúdo."]);
            var items = OpenLibraryReadingOutlineProvider.Parse(source.Document);
            return new(items.Count > 0 ? "Candidate" : "Missing", work, items, [candidate],
                [items.Count > 0 ? "Sumário candidato da edição: confira títulos, ordem e completude antes de importar pelo editor existente."
                    : "A edição consultada não forneceu sumário. Isso não comprova ausência em outras edições da obra."]);
        }
        catch (Exception exception) when (!caller.IsCancellationRequested && IsSourceFailure(exception))
        {
            return new("Failed", null, [], [], ["Não foi possível conferir o sumário agora. Nenhum capítulo foi presumido; tente novamente mais tarde."]);
        }
    }

    private async Task<ReferenceDiscovery> ReferencesAsync(SourceBookIdentity book, CancellationToken token, CancellationToken caller)
    {
        try
        {
            var language = book.Language is "en" or "eng" ? "en" : "pt";
            using var search = await ReadAsync("discovery-wikidata",
                $"w/api.php?action=wbsearchentities&format=json&type=item&language={language}&uselang={language}&limit=3&maxlag=5&search={Uri.EscapeDataString(book.Title)}", token);
            if (search is null) throw new JsonException();
            var ids = Array(search.Document.RootElement, "search").Select(item => String(item, "id"))
                .Where(value => value is not null && EntityId.IsMatch(value)).Take(3).Distinct().ToArray();
            if (ids.Length == 0) return new([], ["A busca no Wikidata não encontrou referências candidatas. Isso não é prova de inexistência de fontes."], false);
            using var source = await ReadAsync("discovery-wikidata",
                $"w/api.php?action=wbgetentities&format=json&props=labels%7Cclaims%7Csitelinks&languages=pt%7Cen&sitefilter=ptwiki%7Cenwiki&maxlag=5&ids={string.Join("%7C", ids)}", token);
            if (source is null || !source.Document.RootElement.TryGetProperty("entities", out var entities)
                || entities.ValueKind != JsonValueKind.Object) throw new JsonException();
            var items = new List<ReferenceItem>();
            var excluded = 0;
            var isbn = BookIsbn.Normalize(book.Isbn13 ?? book.Isbn10);
            foreach (var id in ids)
            {
                if (!entities.TryGetProperty(id!, out var entity) || entity.ValueKind != JsonValueKind.Object
                    || String(entity, "id") != id) continue;
                if (!ClaimValues(entity, "P31").Select(value => String(value, "id")).Any(value => value is not null && BookClasses.Contains(value)))
                { excluded++; continue; }
                var title = Label(entity, language) ?? Label(entity, "en") ?? book.Title;
                var matchesIsbn = isbn.IsValid && isbn.HasValue && ClaimValues(entity, "P212").Concat(ClaimValues(entity, "P957"))
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Any(value => BookIsbn.Normalize(value.GetString()).Canonical == isbn.Canonical);
                var works = ClaimValues(entity, "P648").Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()!).Where(value => WorkId.IsMatch(value)).Take(20).ToArray();
                var candidates = new List<BookSourceCandidate>
                {
                    new("Wikidata", id!, title, $"https://www.wikidata.org/wiki/{id}", "WorkReference", "UnverifiedWork",
                        language, "Wikidata — CC0", source.At, source.Hash)
                };
                if (entity.TryGetProperty("sitelinks", out var links) && links.ValueKind == JsonValueKind.Object)
                    foreach (var site in new[] { "pt", "en" })
                        if (links.TryGetProperty($"{site}wiki", out var link) && String(link, "title") is { Length: > 0 and <= 300 } page)
                            candidates.Add(new("Wikipedia", id!, page,
                                $"https://{site}.wikipedia.org/wiki/{Uri.EscapeDataString(page.Replace(' ', '_'))}",
                                "GeneralArticle", "UnverifiedWork", site,
                                "Wikipedia — autores e licença na página; link descoberto via Wikidata",
                                source.At, source.Hash));
                items.Add(new(matchesIsbn, works, candidates));
            }
            var diagnostics = new List<string>();
            if (excluded > 0) diagnostics.Add($"{excluded} resultado(s) descartado(s) por não possuir tipo literário suportado. Filmes, jogos e tipos não reconhecidos não foram tratados como livros.");
            if (items.Count == 0) diagnostics.Add("Nenhuma referência literária suportada foi confirmada na amostra de até três resultados do Wikidata.");
            return new(items, diagnostics, false);
        }
        catch (Exception exception) when (!caller.IsCancellationRequested && IsSourceFailure(exception))
        {
            return new([], ["Wikidata indisponível ou resposta inválida. Referências não foram inventadas; tente novamente mais tarde."], true);
        }
    }

    private async Task<SourceResponse?> ReadAsync(string clientName, string path, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        token = deadline.Token;
        using var client = createClient(clientName);
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode(); // Includes 429/maxlag; never retry automatically.
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new JsonException();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + count > MaxResponseBytes) throw new JsonException();
            bytes.Write(buffer, 0, count);
        }
        var data = bytes.ToArray();
        var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty("error", out _))
        { document.Dispose(); throw new JsonException(); }
        return new(document, clock.GetUtcNow(), Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    private static bool IsSourceFailure(Exception exception) => exception is HttpRequestException
        or JsonException or OperationCanceledException or ReadingOutlineLimitExceededException;
    private static string? String(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() is { Length: <= 300 } text ? text : null : null;
    private static IEnumerable<JsonElement> Array(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static IEnumerable<JsonElement> ClaimValues(JsonElement item, string property)
    {
        if (!item.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Object) yield break;
        foreach (var claim in Array(claims, property))
            if (claim.ValueKind == JsonValueKind.Object && claim.TryGetProperty("mainsnak", out var snak) && snak.ValueKind == JsonValueKind.Object
                && snak.TryGetProperty("datavalue", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("value", out var value)) yield return value;
    }
    private static string? Label(JsonElement entity, string language) => entity.TryGetProperty("labels", out var labels)
        && labels.ValueKind == JsonValueKind.Object && labels.TryGetProperty(language, out var label) ? String(label, "value") : null;
    private sealed record SourceResponse(JsonDocument Document, DateTimeOffset At, string Hash) : IDisposable
    { public void Dispose() => Document.Dispose(); }
    private sealed record OutlineDiscovery(string Status, string? WorkId, IReadOnlyList<ExternalReadingOutlineItem> Items,
        IReadOnlyList<BookSourceCandidate> Sources, IReadOnlyList<string> Diagnostics);
    private sealed record ReferenceItem(bool IsbnMatch, IReadOnlyList<string> WorkIds, IReadOnlyList<BookSourceCandidate> Sources);
    private sealed record ReferenceDiscovery(IReadOnlyList<ReferenceItem> Items, IReadOnlyList<string> Diagnostics, bool Failed);
}
