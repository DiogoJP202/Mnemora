using System.Text.RegularExpressions;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public sealed record MemoryPackPublicCredits(string Url, string Attribution);

public static class MemoryPackCreditsPolicy
{
    private static readonly HashSet<string> IndexPages =
    [
        "Guia dos Trouxas para Harry Potter/Livros/A Pedra Filosofal",
        "Guia dos Trouxas para Harry Potter/Livros/A Câmara Secreta",
        "Guia dos Trouxas para Harry Potter/Livros/O Prisioneiro de Azkaban"
    ];

    // Credit metadata must not introduce an unrestricted future-bearing URL or text
    // into reader DTOs. Only the collector's fixed, narrative-free attribution qualifies.
    public static MemoryPackPublicCredits? Read(Book book, bool published)
    {
        if (!published || book.ReadingOutlineAttribution != SourcePreparationStore.Attribution
            || !Uri.TryCreate(book.ReadingOutlineSourceUrl, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "pt.wikibooks.org" || !url.IsDefaultPort
            || url.UserInfo.Length != 0 || url.AbsolutePath != "/w/index.php") return null;
        var fields = url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToArray();
        if (fields.Length != 2 || fields.Any(field => field.Length != 2)
            || fields.Count(field => field[0] == "title") != 1 || fields.Count(field => field[0] == "oldid") != 1) return null;
        var title = Uri.UnescapeDataString(fields.Single(field => field[0] == "title")[1].Replace('+', ' '));
        var revision = fields.Single(field => field[0] == "oldid")[1];
        if (!IndexPages.Contains(title) || !Regex.IsMatch(revision, "^[1-9][0-9]{0,18}$", RegexOptions.CultureInvariant)) return null;
        return new($"https://pt.wikibooks.org/w/index.php?title={Uri.EscapeDataString(title)}&oldid={revision}",
            SourcePreparationStore.Attribution);
    }
}
