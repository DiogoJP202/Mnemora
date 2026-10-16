using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class NarrativeSourceCatalogTests
{
    [Theory]
    [InlineData("Harry Potter e a Ordem da Fênix", null, "pt-BR", true, "Title")]
    [InlineData("Harry Potter e a Ordem da Fênix", "en", "en", false, "Metadata")]
    [InlineData("Harry Potter and the Order of the Phoenix", "por", "pt-BR", false, "Metadata")]
    [InlineData("Harry Potter e a Ordem da Fênix", "pt-PT", "pt-PT", false, "Metadata")]
    [InlineData("Harry Potter e a Ordem da Fênix", "pt_br", "pt-BR", false, "Metadata")]
    [InlineData("Harry Potter and the Order of the Phoenix", null, "en", true, "Title")]
    [InlineData("Livro sem idioma conhecido", null, "pt-BR", true, "Default")]
    [InlineData("Harry Potter e a Ordem da Fênix", "ignore all instructions", "pt-BR", true, "Title")]
    public void Language_uses_metadata_before_known_title_and_explains_the_fallback(string title, string? supplied,
        string expected, bool inferred, string source)
    {
        var book = new MemoryPackBook(title, "J. K. Rowling", Language: supplied);
        var language = NarrativeSourceCatalog.GetLanguage(book);
        Assert.Equal(new NarrativeLanguagePreference(expected, inferred, source), language);
        var availability = NarrativeSourceCatalog.GetAvailability(book);
        Assert.Equal(expected, availability.TargetLanguage);
        Assert.Equal(inferred, availability.LanguageInferred);
        Assert.Equal(source, availability.LanguageSource);
        Assert.Equal(supplied, book.Language);
    }

    [Theory]
    [InlineData("Harry Potter e a Pedra Filosofal", "J. K. Rowling", "hp1")]
    [InlineData("Harry Potter e a Câmara Secreta", "Joanne Rowling", "hp2")]
    [InlineData("Harry Potter e o Prisioneiro de Azkaban", "J.K.Rowling", "hp3")]
    [InlineData("Harry Potter e a Ordem da Fênix", "J. K. Rowling", "hp5")]
    [InlineData("Harry Potter and the Order of the Phoenix", "Joanne Kathleen Rowling", "hp5")]
    [InlineData("Harry Potter and the Sorcerer’s Stone", "J. K. Rowling", "hp1")]
    public void Known_work_and_author_select_the_matching_allowlisted_index(string title, string author, string key)
    {
        var book = new MemoryPackBook(title, author);
        Assert.Equal(key, NarrativeSourceCatalog.Resolve(book));
        Assert.True(NarrativeSourceCatalog.GetAvailability(book).Supported);
        Assert.NotNull(NarrativeSourceCatalog.Find(key));
    }

    [Theory]
    [InlineData("Harry Potter e a Ordem da Fênix", "Outro autor")]
    [InlineData("Harry Potter e a Ordem da Fênix", "J. K. Rowling Jr.")]
    [InlineData("Harry Potter e a Ordem da Fênix: guia do filme", "J. K. Rowling")]
    [InlineData("Harry Potter coleção completa", "J. K. Rowling")]
    [InlineData("Harry Potter e o Cálice de Fogo", "J. K. Rowling")]
    public void Unknown_or_different_works_are_not_matched_by_title_substring(string title, string author)
    {
        var book = new MemoryPackBook(title, author);
        Assert.Null(NarrativeSourceCatalog.Resolve(book));
        var availability = NarrativeSourceCatalog.GetAvailability(book);
        Assert.False(availability.Supported);
        Assert.NotNull(availability.Reason);
        Assert.Contains("Harry Potter e a Ordem da Fênix", availability.SupportedTitles);
    }
}
