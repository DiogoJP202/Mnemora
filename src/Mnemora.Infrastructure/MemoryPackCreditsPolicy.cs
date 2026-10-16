using System.Text.RegularExpressions;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public sealed record MemoryPackPublicCredits(string Url, string Attribution);

public static class MemoryPackCreditsPolicy
{
    // Credit metadata must not introduce an unrestricted future-bearing URL or text
    // into reader DTOs. Only the collector's fixed, narrative-free attribution qualifies.
    public static MemoryPackPublicCredits? Read(Book book, bool published)
    {
        if (!published || book.ReadingOutlineAttribution != SourcePreparationStore.Attribution
            || !Uri.TryCreate(book.ReadingOutlineSourceUrl, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host is not ("pt.wikibooks.org" or "en.wikibooks.org") || !url.IsDefaultPort
            || url.UserInfo.Length != 0 || url.AbsolutePath != "/w/index.php") return null;
        var fields = url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToArray();
        if (fields.Length != 2 || fields.Any(field => field.Length != 2)
            || fields.Count(field => field[0] == "title") != 1 || fields.Count(field => field[0] == "oldid") != 1) return null;
        var title = Uri.UnescapeDataString(fields.Single(field => field[0] == "title")[1].Replace('+', ' '));
        var revision = fields.Single(field => field[0] == "oldid")[1];
        if (!NarrativeSourceCatalog.IsIndex(url.Host[..2], title)
            || !Regex.IsMatch(revision, "^[1-9][0-9]{0,18}$", RegexOptions.CultureInvariant)) return null;
        return new($"https://{url.Host}/w/index.php?title={Uri.EscapeDataString(title)}&oldid={revision}",
            SourcePreparationStore.Attribution);
    }
}
