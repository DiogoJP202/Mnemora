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
}
