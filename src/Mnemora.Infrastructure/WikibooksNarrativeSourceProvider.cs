using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

// Only the configured study-guide chapters are supported. Article links are data,
// never fetch targets; the HTML parser has no resource loader or script engine.
public sealed class WikibooksNarrativeSourceProvider(Func<string, HttpClient> createClient, TimeProvider clock)
    : IBookNarrativeSourceProvider
{
    public const string License = "https://creativecommons.org/licenses/by-sa/4.0/";
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxTextBytes = 512 * 1024;
    private const string PtPrefix = "Guia dos Trouxas para Harry Potter/Livros/";
    private const string EnPrefix = "Muggles' Guide to Harry Potter/Books/";
    private static readonly Regex WhiteSpace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ChapterSuffix = new(@"^(?:Capítulo|Chapter) ?([1-9][0-9]?)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex LabelPrefix = new(@"^Capítulo\s+[0-9]+\s*[:—–-]\s*",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex HiddenStyle = new(@"(?:display\s*:\s*none|visibility\s*:\s*hidden)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly IReadOnlyDictionary<string, Work> Works = new Dictionary<string, Work>(StringComparer.Ordinal)
    {
        ["hp1"] = new("Harry Potter e a Pedra Filosofal", "A Pedra Filosofal", "Philosopher's Stone", 17),
        ["hp2"] = new("Harry Potter e a Câmara Secreta", "A Câmara Secreta", "Chamber of Secrets", 18),
        ["hp3"] = new("Harry Potter e o Prisioneiro de Azkaban", "O Prisioneiro de Azkaban", "Prisoner of Azkaban", 22)
    };

    public async Task<NarrativeSourceIndex> GetIndexAsync(string bookKey, CancellationToken cancellationToken)
    {
        var work = WorkFor(bookKey);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var page = PtPrefix + work.Pt;
            using var metadata = await MetadataAsync("pt", page, deadline.Token);
            if (metadata is null || FindSection(metadata.Document.RootElement.GetProperty("parse"), "Capítulos", page) is not { } section)
                throw Failure("source_index_missing", "O índice público não pôde ser confirmado. Nenhum capítulo foi presumido.");
            using (var content = await SectionAsync("pt", metadata, section, deadline.Token))
            {
                if (content is null) throw Failure("source_index_missing", "A seção do índice público não está disponível.");
                var chapters = ParseIndex(Html(content), page, work.Count);
                return new(new(bookKey, work.Title, page, "pt", License), chapters,
                    metadata.Revision, SourceUrl("pt", page, metadata.Revision, "Capítulos"),
                    ["Índice do guia comunitário, não do ISBN cadastrado. Confira a correspondência com a tradução e a edição.",
                     "Somente Sinopse/Synopsis será coletada. Análises e páginas de personagens não são usadas; a revisão editorial continua necessária."]);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Failure("source_timeout", "A consulta do índice excedeu o tempo disponível; tente novamente explicitamente.", 504); }
    }

    public async Task<CollectedNarrativeSource?> GetChapterAsync(NarrativeSourceIndex index,
        NarrativeChapter chapter, CancellationToken cancellationToken)
    {
        var work = WorkFor(index.Book.Key);
        var ptPage = PtPrefix + work.Pt;
        if (index.Book.IndexPage != ptPage || index.Book.Language != "pt"
            || chapter.Order < 1 || chapter.Order > work.Count
            || !MatchesChapter(chapter.PageTitle, ptPage, chapter.Order)
            || index.Chapters.Count != work.Count
            || !index.Chapters.Any(item => item == chapter))
            throw Failure("source_identity_mismatch", "O capítulo não pertence ao índice permitido.", 409);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var collected = await ChapterAsync("pt", chapter.PageTitle, "Sinopse", chapter, deadline.Token);
            // Missing pages/sections or unusable editorial markup can fall back to
            // the same numbered chapter. Operational failures never change source.
            return collected ?? await ChapterAsync("en", $"{EnPrefix}{work.En}/Chapter {chapter.Order}",
                "Synopsis", chapter, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Failure("source_timeout", "A coleta do capítulo excedeu o tempo disponível; o progresso anterior foi preservado.", 504); }
    }

    private async Task<CollectedNarrativeSource?> ChapterAsync(string language, string page, string heading,
        NarrativeChapter chapter, CancellationToken token)
    {
        using var metadata = await MetadataAsync(language, page, token);
        if (metadata is null) return null;
        var section = FindSection(metadata.Document.RootElement.GetProperty("parse"), heading, page);
        if (section is null) return null;
        using var content = await SectionAsync(language, metadata, section, token);
        if (content is null) return null;
        var text = ParseNarrative(Html(content), heading);
        if (text is null) return null;
        if (Encoding.UTF8.GetByteCount(text) > MaxTextBytes)
            throw Failure("source_text_too_large", "A sinopse excede o limite permitido e não foi truncada.");
        var attribution = $"Adaptado dos colaboradores do Wikibooks ({language}), CC BY-SA 4.0 ({License}). "
            + "Extração e organização por IA; revisões e histórico de autoria no relatório de fontes.";
        return new(chapter.Order, chapter.Title, text,
            new($"capitulo-{chapter.Order:000}", "Wikibooks", SourceUrl(language, page, metadata.Revision, heading),
                metadata.Revision, metadata.PageId, language, License, attribution,
                content.RetrievedAt, content.Hash, Hash(Encoding.UTF8.GetBytes(text))));
    }

    private async Task<Snapshot?> MetadataAsync(string language, string page, CancellationToken token)
    {
        var snapshot = await ReadAsync(language,
            $"action=parse&page={Uri.EscapeDataString(page)}&prop=tocdata%7Crevid", token);
        if (snapshot is null) return null;
        if (!SameTitle(snapshot.Title, page))
        {
            snapshot.Dispose();
            throw Failure("source_identity_mismatch", "A fonte retornou uma página diferente da solicitada.", 409);
        }
        return snapshot;
    }

    private async Task<Snapshot?> SectionAsync(string language, Snapshot metadata, string section, CancellationToken token)
    {
        var result = await ReadAsync(language,
            $"action=parse&oldid={metadata.Revision}&section={section}&prop=text%7Crevid&disableeditsection=1&disabletoc=1&disablelimitreport=1", token);
        if (result is not null && (result.Revision != metadata.Revision || result.PageId != metadata.PageId
            || !SameTitle(result.Title, metadata.Title)))
        {
            result.Dispose();
            throw Failure("source_revision_mismatch", "A revisão retornada não corresponde à fonte conferida.", 409);
        }
        return result;
    }

    private async Task<Snapshot?> ReadAsync(string language, string query, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var client = createClient($"narrative-wikibooks-{language}");
        if (client.BaseAddress is not { Scheme: "https" } address
            || address.Host != $"{language}.wikibooks.org" || address.AbsolutePath != "/"
            || !address.IsDefaultPort || address.UserInfo.Length != 0)
            throw Failure("source_configuration", "O serviço de fontes está configurado incorretamente.", 503);
        try
        {
            using var response = await client.GetAsync($"w/api.php?{query}&format=json&formatversion=2&maxlag=5",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new GenerationException("source_rate_limited", "A fonte pública limitou as consultas. Aguarde antes de retomar.", 429, RetryAfter(response));
            if (!response.IsSuccessStatusCode)
                throw Failure("source_unavailable", "A fonte pública está indisponível. Nenhuma fonte alternativa foi consultada.", 502);
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw Failure("source_response_too_large", "A resposta pública excede o limite permitido.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    throw Failure("source_response_too_large", "A resposta pública excede o limite permitido.");
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.ToArray();
            var document = JsonDocument.Parse(bytes, new() { MaxDepth = 64 });
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
                if (root.TryGetProperty("error", out var error))
                {
                    var code = String(error, "code");
                    if (code is "missingtitle" or "nosuchsection") { document.Dispose(); return null; }
                    if (code is "ratelimited" or "maxlag")
                        throw new GenerationException("source_rate_limited", "A fonte pública pediu uma pausa antes da próxima consulta.", 429, RetryAfter(response));
                    throw new JsonException();
                }
                if (!root.TryGetProperty("parse", out var parse) || parse.ValueKind != JsonValueKind.Object
                    || !parse.TryGetProperty("revid", out var revision) || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var revisionId) || revisionId <= 0
                    || !parse.TryGetProperty("pageid", out var page) || page.ValueKind != JsonValueKind.Number || !page.TryGetInt64(out var pageId) || pageId <= 0
                    || String(parse, "title") is not { Length: > 0 } title)
                    throw new JsonException();
                return new(document, title, revisionId.ToString(CultureInfo.InvariantCulture),
                    pageId.ToString(CultureInfo.InvariantCulture), Hash(bytes), clock.GetUtcNow());
            }
            catch { document.Dispose(); throw; }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw Failure("source_timeout", "A fonte pública demorou para responder; retome a coleta explicitamente.", 504); }
        catch (JsonException)
        { throw Failure("source_invalid_response", "A fonte pública retornou uma resposta inválida."); }
        catch (HttpRequestException)
        { throw Failure("source_unavailable", "Não foi possível acessar a fonte pública agora.", 502); }
        catch (IOException)
        { throw Failure("source_unavailable", "A conexão com a fonte pública foi interrompida.", 502); }
    }

    private static IReadOnlyList<NarrativeChapter> ParseIndex(string html, string indexPage, int expectedCount)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var chapters = new Dictionary<int, NarrativeChapter>();
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var page = LinkedPage(anchor.GetAttribute("href"));
            if (page is null || !page.StartsWith(indexPage + "/", StringComparison.Ordinal)) continue;
            var suffix = page[(indexPage.Length + 1)..];
            var match = ChapterSuffix.Match(suffix);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var order)) continue;
            if (order < 1 || order > expectedCount)
                throw Failure("source_index_incomplete", "O índice público diverge da sequência conhecida da obra.");
            var label = WhiteSpace.Replace(anchor.TextContent, " ").Trim();
            var title = LabelPrefix.Replace(label, "").Trim();
            if (title.Length is 0 or > 200) title = $"Capítulo {order}";
            var chapter = new NarrativeChapter(order, title, page);
            if (chapters.TryGetValue(order, out var prior) && prior != chapter)
                throw Failure("source_index_ambiguous", "O índice público contém capítulos duplicados ou ambíguos.");
            chapters[order] = chapter;
        }
        if (chapters.Count != expectedCount || Enumerable.Range(1, expectedCount).Any(order => !chapters.ContainsKey(order)))
            throw Failure("source_index_incomplete", "O índice público não contém a sequência esperada. Nenhum capítulo foi inventado.");
        return chapters.Values.OrderBy(item => item.Order).ToArray();
    }

    private static string? ParseNarrative(string html, string heading)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var headings = document.QuerySelectorAll("h1,h2,h3,h4,h5,h6");
        if (headings.Length != 1 || headings[0].LocalName != "h2" || headings[0].TextContent.Trim() != heading) return null;
        foreach (var item in document.QuerySelectorAll("script,style,link,meta,noscript,iframe,object,embed,svg,math,img,picture,figure,table,nav,aside,header,footer,form,input,button,textarea,select,sup,.mw-editsection,.noprint,.metadata,.navbox,.thumb,.toc,.ambox,.hatnote,.reference,.mw-collapsible,.mw-collapsed,.hidden,.visually-hidden,.sr-only,[hidden],[aria-hidden=true]")) item.Remove();
        foreach (var item in document.QuerySelectorAll("[style]"))
            if (HiddenStyle.IsMatch(item.GetAttribute("style") ?? "")) item.Remove();
        var paragraphs = new List<string>();
        foreach (var item in document.QuerySelectorAll("p,li,blockquote"))
        {
            var nested = false;
            for (var parent = item.ParentElement; parent is not null; parent = parent.ParentElement)
                if (parent.LocalName is "p" or "li" or "blockquote") { nested = true; break; }
            if (nested) continue;
            var text = new StringBuilder();
            var nodes = new Stack<INode>();
            nodes.Push(item);
            var visited = 0;
            while (nodes.TryPop(out var node))
            {
                if (++visited > 100_000) throw Failure("source_markup_too_large", "A sinopse possui marcação excessiva.");
                if (node is IText value) text.Append(value.Data);
                else
                {
                    if (node is IElement { LocalName: "br" or "p" or "li" or "blockquote" }) text.Append(' ');
                    for (var child = node.LastChild; child is not null; child = child.PreviousSibling) nodes.Push(child);
                }
            }
            var normalized = WhiteSpace.Replace(text.ToString(), " ").Trim();
            if (normalized.Length > 0) paragraphs.Add(normalized);
        }
        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static string? FindSection(JsonElement parse, string heading, string page)
    {
        if (!parse.TryGetProperty("tocdata", out var toc) || toc.ValueKind != JsonValueKind.Object
            || !toc.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array) return null;
        var matches = sections.EnumerateArray().Where(section => section.ValueKind == JsonValueKind.Object
            && String(section, "line") == heading && section.TryGetProperty("hLevel", out var level)
            && level.ValueKind == JsonValueKind.Number && level.TryGetInt32(out var number) && number == 2).ToArray();
        if (matches.Length != 1 || String(matches[0], "fromTitle") is not { } fromTitle || !SameTitle(fromTitle, page)
            || String(matches[0], "index") is not { } index || !int.TryParse(index, out var position) || position <= 0) return null;
        return position.ToString(CultureInfo.InvariantCulture);
    }

    private static string? LinkedPage(string? href)
    {
        if (href is null || href.Contains('#')) return null;
        if (!Uri.TryCreate(new Uri("https://pt.wikibooks.org/"), href, out var uri)
            || uri.Scheme != "https" || uri.Host != "pt.wikibooks.org" || !uri.IsDefaultPort) return null;
        if (uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal))
            return Uri.UnescapeDataString(uri.AbsolutePath[6..]).Replace('_', ' ');
        if (uri.AbsolutePath != "/w/index.php") return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "title")
                return Uri.UnescapeDataString(parts[1].Replace('+', ' ')).Replace('_', ' ');
        }
        return null;
    }

    private static bool MatchesChapter(string page, string prefix, int order) => page.StartsWith(prefix + "/", StringComparison.Ordinal)
        && ChapterSuffix.Match(page[(prefix.Length + 1)..]) is { Success: true } match
        && int.TryParse(match.Groups[1].Value, out var number) && number == order;
    private static Work WorkFor(string key) => Works.TryGetValue(key, out var work) ? work
        : throw Failure("source_unsupported", "Ainda não há um coletor por capítulo para esta obra.", 400);
    private static string Html(Snapshot snapshot) => String(snapshot.Document.RootElement.GetProperty("parse"), "text")
        ?? throw Failure("source_invalid_response", "A fonte não retornou o conteúdo da seção.");
    private static string? String(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool SameTitle(string first, string second) => first.Replace('_', ' ') == second.Replace('_', ' ');
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string SourceUrl(string language, string page, string revision, string section) =>
        $"https://{language}.wikibooks.org/w/index.php?title={Uri.EscapeDataString(page)}&oldid={revision}#{Uri.EscapeDataString(section)}";
    private int RetryAfter(HttpResponseMessage response) => (int)Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds
        ?? (response.Headers.RetryAfter?.Date is { } date ? (date - clock.GetUtcNow()).TotalSeconds : 60), 1, 3600);
    private static GenerationException Failure(string code, string message, int status = 502) => new(code, message, status);
    private sealed record Work(string Title, string Pt, string En, int Count);
    private sealed record Snapshot(JsonDocument Document, string Title, string Revision, string PageId,
        string Hash, DateTimeOffset RetrievedAt) : IDisposable
    { public void Dispose() => Document.Dispose(); }
}
