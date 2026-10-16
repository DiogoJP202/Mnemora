using System.Globalization;
using System.Text;
using Mnemora.Application;

namespace Mnemora.Infrastructure;

public sealed record NarrativeSourceDefinition(string Key, string Title, string PortugueseTitle,
    string EnglishTitle, int ChapterCount, string IndexLanguage = "pt")
{
    public string IndexPage => IndexLanguage == "en" ? EnglishPage : PortuguesePage;
    public string PortuguesePage => $"Guia dos Trouxas para Harry Potter/Livros/{PortugueseTitle}";
    public string EnglishPage => $"Muggles' Guide to Harry Potter/Books/{EnglishTitle}";
}

// One allowlist drives work identification, HTTP targets, availability and public credits.
public static class NarrativeSourceCatalog
{
    private static readonly NarrativeSourceDefinition[] Works =
    [
        new("hp1", "Harry Potter e a Pedra Filosofal", "A Pedra Filosofal", "Philosopher's Stone", 17),
        new("hp2", "Harry Potter e a Câmara Secreta", "A Câmara Secreta", "Chamber of Secrets", 18),
        new("hp3", "Harry Potter e o Prisioneiro de Azkaban", "O Prisioneiro de Azkaban", "Prisoner of Azkaban", 22),
        new("hp5", "Harry Potter e a Ordem da Fênix", "A Ordem da Fênix", "Order of the Phoenix", 38, "en")
    ];
    private static readonly IReadOnlyList<string> Titles = Array.AsReadOnly(Works.Select(work => work.Title).ToArray());
    public const string UnsupportedReason = "Ainda não há coleta automática por capítulo para esta obra. Confira os títulos com fonte disponível; a entrada manual continua disponível.";

    public static NarrativeSourceDefinition? Find(string key) => Works.FirstOrDefault(work => work.Key == key);
    public static bool IsIndex(string language, string page) => Works.Any(work =>
        work.IndexLanguage == language && work.IndexPage == page);
    public static SourcePreparationAvailability GetAvailability(MemoryPackBook book)
    {
        var supported = Resolve(book) is not null;
        return new(supported, supported ? null : UnsupportedReason, Titles);
    }

    public static string? Resolve(MemoryPackBook book)
    {
        if (Fold(book.Author) is not ("jkrowling" or "joannerowling" or "joannekathleenrowling")) return null;
        var title = Fold(book.Title);
        return Works.FirstOrDefault(work => title == Fold(work.Title)
            || title == Fold($"Harry Potter and the {work.EnglishTitle}"))?.Key
            ?? (title == "harrypotterandthesorcerersstone" ? "hp1" : null);
    }
    private static string Fold(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)))
        .ToLowerInvariant();
}
