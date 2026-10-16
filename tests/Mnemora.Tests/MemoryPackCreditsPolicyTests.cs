using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackCreditsPolicyTests
{
    private const string Page = "Guia dos Trouxas para Harry Potter/Livros/A Pedra Filosofal";
    private static Book Book(string? url = null) => new() { Title = "Harry Potter e a Pedra Filosofal", Author = "J. K. Rowling",
        ReadingOutlineAttribution = SourcePreparationStore.Attribution,
        ReadingOutlineSourceUrl = url ?? $"https://pt.wikibooks.org/w/index.php?title={Uri.EscapeDataString(Page)}&oldid=421803" };
    [Fact]
    public void Credits_are_publication_gated_and_never_expose_arbitrary_attribution()
    {
        var book = Book();
        Assert.Null(MemoryPackCreditsPolicy.Read(book, false));
        var credits = MemoryPackCreditsPolicy.Read(book, true);
        Assert.NotNull(credits);
        Assert.Contains("CC BY-SA 4.0", credits.Attribution);
        book.ReadingOutlineAttribution = "A identidade secreta de uma personagem aparece no último capítulo.";
        Assert.Null(MemoryPackCreditsPolicy.Read(book, true));
    }
    [Theory]
    [InlineData("http://pt.wikibooks.org/w/index.php?title=anything&oldid=1")]
    [InlineData("https://evil.example/w/index.php?title=anything&oldid=1")]
    [InlineData("https://pt.wikibooks.org/w/index.php?title=Personagem_futura&oldid=1")]
    [InlineData("https://pt.wikibooks.org/w/index.php?title=anything&oldid=1&secret=spoiler")]
    [InlineData("https://en.wikibooks.org/w/index.php?title=Guia%20dos%20Trouxas%20para%20Harry%20Potter%2FLivros%2FA%20Pedra%20Filosofal&oldid=1")]
    public void Untrusted_or_future_bearing_urls_are_not_reader_metadata(string url) =>
        Assert.Null(MemoryPackCreditsPolicy.Read(Book(url), true));
    [Fact]
    public void Returned_url_is_canonical_and_drops_untrusted_fragment()
    {
        var book = Book(); book.ReadingOutlineSourceUrl += "#Identidade_futura";
        var credits = MemoryPackCreditsPolicy.Read(book, true);
        Assert.NotNull(credits);
        Assert.DoesNotContain("Identidade", credits.Url);
        Assert.DoesNotContain("#", credits.Url);
    }

    [Fact]
    public void Fifth_book_can_credit_its_allowlisted_english_index_without_exposing_the_fragment()
    {
        var page = "Muggles' Guide to Harry Potter/Books/Order of the Phoenix";
        var book = Book($"https://en.wikibooks.org/w/index.php?title={Uri.EscapeDataString(page)}&oldid=4409182#Future_identity");
        var credits = MemoryPackCreditsPolicy.Read(book, true);
        Assert.NotNull(credits);
        Assert.StartsWith("https://en.wikibooks.org/", credits.Url);
        Assert.DoesNotContain("Future", credits.Url);
        Assert.Null(MemoryPackCreditsPolicy.Read(book, false));
    }
}
