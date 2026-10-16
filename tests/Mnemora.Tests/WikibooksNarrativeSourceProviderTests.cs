using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class WikibooksNarrativeSourceProviderTests
{
    [Theory]
    [InlineData("hp1", 17)]
    [InlineData("hp2", 18)]
    [InlineData("hp3", 22)]
    [InlineData("hp5", 38)]
    public async Task Index_uses_only_allowlisted_section_orders_and_exact_linked_chapter_paths(string key, int count)
    {
        using var fixture = new Fixture(key);
        var index = await fixture.Provider.GetIndexAsync(key, default);
        Assert.Equal(count, index.Chapters.Count);
        Assert.Equal(Enumerable.Range(1, count), index.Chapters.Select(chapter => chapter.Order));
        Assert.Equal(key == "hp5" ? "en" : "pt", index.Book.Language);
        Assert.Equal("1000", index.RevisionId);
        Assert.Contains("oldid=1000", index.SourceUrl);
        Assert.Equal("Unidade fictícia 1", index.Chapters[0].Title);
        if (key == "hp3") Assert.EndsWith("/Capítulo11", index.Chapters[10].PageTitle);
        Assert.Equal(2, fixture.Calls.Count);
        Assert.Contains("prop=tocdata", fixture.Calls[0].Query);
        Assert.Contains("oldid=1000", fixture.Calls[1].Query);
        Assert.Contains("section=1", fixture.Calls[1].Query);
    }

    [Fact]
    public async Task Fifth_book_follows_the_english_index_when_english_is_preferred()
    {
        using var fixture = new Fixture("hp5") { MappingMode = "rate-limit" };
        var index = await fixture.Provider.GetIndexAsync("hp5", default, "en");
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("en", result.Provenance.Language);
        Assert.Contains("Order%20of%20the%20Phoenix", result.Provenance.Url);
        Assert.Equal("Ari sees a lantern.", result.Text);
        Assert.Equal("en", index.PreferredLanguage);
        Assert.All(fixture.Calls, uri => Assert.Equal("en.wikibooks.org", uri.Host));
        Assert.DoesNotContain(fixture.Calls, uri => uri.Query.Contains("list=allpages", StringComparison.Ordinal));
        Assert.Equal(4, fixture.Calls.Count);
    }

    [Fact]
    public async Task Fifth_book_prefers_only_the_portuguese_page_confirmed_by_the_bounded_listing()
    {
        using var fixture = new Fixture("hp5");
        var index = await fixture.Provider.GetIndexAsync("hp5", default, "pt");
        Assert.Equal(38, index.Chapters.Count);
        Assert.Equal(fixture.PortuguesePage + "/Capítulo 1", index.Chapters[0].PortuguesePageTitle);
        Assert.Contains("aplimit=65", fixture.Calls[2].Query);
        Assert.Contains("apnamespace=0", fixture.Calls[2].Query);
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("pt", result.Provenance.Language);
        Assert.Equal("Ari encontra Lia & sua carta.\n\nA pista parece incerta.", result.Text);
        Assert.All(fixture.Calls.Skip(2), uri => Assert.Equal("pt.wikibooks.org", uri.Host));
        Assert.Equal(5, fixture.Calls.Count);
    }

    [Fact]
    public async Task Unlisted_portuguese_chapter_is_never_guessed_or_requested()
    {
        using var fixture = new Fixture("hp5") { MappingMode = "empty" };
        var index = await fixture.Provider.GetIndexAsync("hp5", default, "pt");
        Assert.All(index.Chapters, chapter => Assert.Null(chapter.PortuguesePageTitle));
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("en", result.Provenance.Language);
        Assert.All(fixture.Calls.Skip(3), uri => Assert.Equal("en.wikibooks.org", uri.Host));
        Assert.Equal(5, fixture.Calls.Count);
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("duplicate")]
    [InlineData("wrong-work")]
    [InlineData("wrong-namespace")]
    [InlineData("unexpected-suffix")]
    [InlineData("out-of-range")]
    [InlineData("invalid-page-id")]
    [InlineData("too-many")]
    public async Task Ambiguous_or_incomplete_portuguese_mapping_is_rejected_without_followup_requests(string mode)
    {
        using var fixture = new Fixture("hp5") { MappingMode = mode };
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("hp5", default, "pt"));
        Assert.Equal("source_index_ambiguous", failure.Code);
        Assert.Equal(3, fixture.Calls.Count);
    }

    [Theory]
    [InlineData("rate-limit", "source_rate_limited")]
    [InlineData("timeout", "source_timeout")]
    public async Task Portuguese_mapping_operational_failure_stops_before_any_narrative_request(string mode, string code)
    {
        using var fixture = new Fixture("hp5") { MappingMode = mode };
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("hp5", default, "pt"));
        Assert.Equal(code, failure.Code);
        Assert.Equal(3, fixture.Calls.Count);
        Assert.Equal("pt.wikibooks.org", fixture.Calls[^1].Host);
    }

    [Theory]
    [InlineData("pt-missing")]
    [InlineData("pt-no-synopsis")]
    [InlineData("pt-extra-analysis")]
    public async Task Fifth_book_can_fall_back_to_its_indexed_english_chapter_only_for_editorial_absence(string mode)
    {
        using var fixture = new Fixture("hp5");
        var index = await fixture.Provider.GetIndexAsync("hp5", default, "pt");
        fixture.Mode = mode;
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("en", result.Provenance.Language);
        Assert.Equal("pt.wikibooks.org", fixture.Calls[3].Host);
        Assert.Equal("en.wikibooks.org", fixture.Calls[^1].Host);
    }

    [Theory]
    [InlineData("rate-limit", "source_rate_limited")]
    [InlineData("timeout", "source_timeout")]
    [InlineData("invalid-json", "source_invalid_response")]
    public async Task Fifth_book_does_not_switch_away_from_portuguese_on_operational_errors(string mode, string code)
    {
        using var fixture = new Fixture("hp5");
        var index = await fixture.Provider.GetIndexAsync("hp5", default, "pt");
        fixture.Mode = mode;
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetChapterAsync(index, index.Chapters[0], default));
        Assert.Equal(code, failure.Code);
        Assert.Equal(4, fixture.Calls.Count);
        Assert.Equal("pt.wikibooks.org", fixture.Calls[^1].Host);
    }

    [Fact]
    public async Task Portuguese_index_can_use_english_directly_when_the_book_prefers_it()
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default, "en");
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("en", result.Provenance.Language);
        Assert.All(fixture.Calls.Skip(2), uri => Assert.Equal("en.wikibooks.org", uri.Host));
        Assert.Equal(4, fixture.Calls.Count);
    }

    [Theory]
    [InlineData("missing-index-chapter")]
    [InlineData("external-index-chapter")]
    [InlineData("duplicate-index-chapter")]
    public async Task Fifth_book_keeps_the_same_complete_index_and_origin_validation(string mode)
    {
        using var fixture = new Fixture("hp5") { Mode = mode };
        await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("hp5", default));
        Assert.All(fixture.Calls, uri => Assert.Equal("en.wikibooks.org", uri.Host));
    }

    [Fact]
    public async Task Unsupported_book_makes_no_network_request()
    {
        using var fixture = new Fixture();
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("http://localhost/secret", default));
        Assert.Equal("source_unsupported", failure.Code);
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task Unsupported_language_makes_no_network_request()
    {
        using var fixture = new Fixture("hp5");
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("hp5", default, "fr"));
        Assert.Equal("source_language_unsupported", failure.Code);
        Assert.Empty(fixture.Calls);
    }

    [Theory]
    [InlineData("missing-index-chapter", "source_index_incomplete")]
    [InlineData("external-index-chapter", "source_index_incomplete")]
    [InlineData("duplicate-index-chapter", "source_index_ambiguous")]
    [InlineData("wrong-index-title", "source_identity_mismatch")]
    [InlineData("changed-index-revision", "source_revision_mismatch")]
    public async Task Incomplete_or_conflicting_index_is_never_guessed(string mode, string code)
    {
        using var fixture = new Fixture { Mode = mode };
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetIndexAsync("hp1", default));
        Assert.Equal(code, failure.Code);
        Assert.All(fixture.Calls, uri => Assert.Equal("pt.wikibooks.org", uri.Host));
    }

    [Fact]
    public async Task Narrative_preserves_only_visible_synopsis_paragraphs_and_provenance()
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("Ari encontra Lia & sua carta.\n\nA pista parece incerta.", result.Text);
        Assert.DoesNotContain("identidade-futura", result.Text);
        Assert.DoesNotContain("segredo", result.Text);
        Assert.DoesNotContain("script", result.Text);
        Assert.Equal("pt", result.Provenance.Language);
        Assert.Equal("2001", result.Provenance.RevisionId);
        Assert.Equal("201", result.Provenance.PageId);
        Assert.Equal("capitulo-001", result.Provenance.UnitSlug);
        Assert.Equal(WikibooksNarrativeSourceProvider.License, result.Provenance.License);
        Assert.Contains("colaboradores do Wikibooks", result.Provenance.Attribution);
        Assert.Contains("oldid=2001", result.Provenance.Url);
        Assert.Equal(Hash(result.Text), result.Provenance.SourceHash);
        Assert.Equal(Hash(fixture.LastJson!), result.Provenance.ResponseHash);
        Assert.Equal(4, fixture.Calls.Count);
        Assert.Contains("section=2", fixture.Calls[3].Query);
    }

    [Theory]
    [InlineData("pt-missing")]
    [InlineData("pt-no-synopsis")]
    [InlineData("pt-ambiguous-synopsis")]
    [InlineData("pt-transcluded-synopsis")]
    [InlineData("pt-wrong-heading-level")]
    [InlineData("pt-extra-analysis")]
    [InlineData("pt-empty-synopsis")]
    public async Task Only_missing_or_unusable_editorial_section_falls_back_to_same_chapter_in_english(string mode)
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        fixture.Mode = mode;
        var result = await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default);
        Assert.NotNull(result);
        Assert.Equal("en", result.Provenance.Language);
        Assert.Equal("Unidade fictícia 1", result.Title);
        Assert.Equal("Ari sees a lantern.", result.Text);
        Assert.Contains("/Chapter 1", Uri.UnescapeDataString(result.Provenance.Url));
        Assert.Contains(fixture.Calls, uri => uri.Host == "en.wikibooks.org");
    }

    [Theory]
    [InlineData("rate-limit", "source_rate_limited", 429)]
    [InlineData("maxlag", "source_rate_limited", 429)]
    [InlineData("timeout", "source_timeout", 504)]
    [InlineData("invalid-json", "source_invalid_response", 502)]
    [InlineData("invalid-revision-type", "source_invalid_response", 502)]
    [InlineData("redirect", "source_unavailable", 502)]
    [InlineData("oversized-header", "source_response_too_large", 502)]
    [InlineData("oversized-body", "source_response_too_large", 502)]
    public async Task Operational_failure_does_not_retry_or_switch_language(string mode, string code, int status)
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        fixture.Mode = mode;
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetChapterAsync(index, index.Chapters[0], default));
        Assert.Equal(code, failure.Code);
        Assert.Equal(status, failure.StatusCode);
        if (mode == "rate-limit") Assert.Equal(45, failure.RetryAfterSeconds);
        Assert.Equal(3, fixture.Calls.Count);
        Assert.DoesNotContain(fixture.Calls, uri => uri.Host == "en.wikibooks.org");
    }

    [Theory]
    [InlineData("changed-chapter-revision", "source_revision_mismatch")]
    [InlineData("wrong-chapter-title", "source_identity_mismatch")]
    public async Task Wrong_identity_or_revision_never_supplies_text(string mode, string code)
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        fixture.Mode = mode;
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetChapterAsync(index, index.Chapters[0], default));
        Assert.Equal(code, failure.Code);
        Assert.DoesNotContain(fixture.Calls, uri => uri.Host == "en.wikibooks.org");
    }

    [Fact]
    public async Task Missing_both_languages_remains_an_explicit_gap()
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        fixture.Mode = "both-missing";
        Assert.Null(await fixture.Provider.GetChapterAsync(index, index.Chapters[0], default));
        Assert.Equal(4, fixture.Calls.Count);
    }

    [Fact]
    public async Task Forged_chapter_cannot_choose_another_source_page()
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        var forged = index.Chapters[0] with { PageTitle = "https://localhost/private" };
        var failure = await Assert.ThrowsAsync<GenerationException>(() => fixture.Provider.GetChapterAsync(index, forged, default));
        Assert.Equal("source_identity_mismatch", failure.Code);
        Assert.Equal(2, fixture.Calls.Count);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_fallback()
    {
        using var fixture = new Fixture();
        var index = await fixture.Provider.GetIndexAsync("hp1", default);
        fixture.Mode = "wait-cancel";
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.GetChapterAsync(index, index.Chapters[0], cancel.Token));
        Assert.Equal(3, fixture.Calls.Count);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // All texts below are invented fixtures. CI never contacts Wikimedia or Groq.
    private sealed class Fixture : IDisposable
    {
        public string Mode { get; set; } = "normal";
        public string MappingMode { get; set; } = "normal";
        public string PortuguesePage => NarrativeSourceCatalog.Find(key)!.PortuguesePage;
        public List<Uri> Calls { get; } = [];
        public string? LastJson { get; private set; }
        public WikibooksNarrativeSourceProvider Provider { get; }
        private readonly Handler handler;
        private readonly string page;
        private readonly int count;
        private readonly string key;

        public Fixture(string key = "hp1")
        {
            this.key = key;
            (page, count) = key switch
            {
                "hp2" => ("Guia dos Trouxas para Harry Potter/Livros/A Câmara Secreta", 18),
                "hp3" => ("Guia dos Trouxas para Harry Potter/Livros/O Prisioneiro de Azkaban", 22),
                "hp5" => ("Muggles' Guide to Harry Potter/Books/Order of the Phoenix", 38),
                _ => ("Guia dos Trouxas para Harry Potter/Livros/A Pedra Filosofal", 17)
            };
            handler = new Handler(this);
            Provider = new(name => new HttpClient(handler, false)
            { BaseAddress = new Uri(name.EndsWith("-en") ? "https://en.wikibooks.org/" : "https://pt.wikibooks.org/") }, TimeProvider.System);
        }

        private HttpResponseMessage Json(object value)
        {
            LastJson = JsonSerializer.Serialize(value);
            return new(HttpStatusCode.OK) { Content = new StringContent(LastJson, Encoding.UTF8, "application/json") };
        }

        private HttpResponseMessage Parse(string title, int revision, int pageId, object? toc = null, string? text = null) =>
            Json(new { parse = new { title, revid = revision, pageid = pageId, tocdata = toc, text } });

        private object Toc(string heading, string source, string section = "2", int level = 2) =>
            new { sections = new[] { new { hLevel = level, line = heading, index = section, fromTitle = source.Replace(' ', '_') } } };

        private string IndexHtml()
        {
            var chapterLabel = key == "hp5" ? "Chapter" : "Capítulo";
            var html = new StringBuilder($"<div><h2>{(key == "hp5" ? "Chapters" : "Capítulos")}</h2>");
            for (var order = 1; order <= count; order++)
            {
                if (Mode == "missing-index-chapter" && order == count) continue;
                var chapterPage = $"{page}/{chapterLabel}{(key == "hp3" && order == 11 ? "" : " ")}{order}";
                var href = order == 2 ? $"/w/index.php?title={Uri.EscapeDataString(chapterPage)}&amp;action=edit&amp;redlink=1"
                    : "/wiki/" + Uri.EscapeDataString(chapterPage.Replace(' ', '_'));
                if (Mode == "external-index-chapter" && order == 1) href = "https://localhost/wiki/" + Uri.EscapeDataString(chapterPage);
                html.Append($"<a href=\"{href}\">{chapterLabel} {order}: Unidade fictícia {order}</a>");
                if (Mode == "duplicate-index-chapter" && order == 1)
                    html.Append($"<a href=\"{href}\">{chapterLabel} 1: Outra unidade</a>");
            }
            return html.Append("</div>").ToString();
        }

        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var uri = request.RequestUri!;
                fixture.Calls.Add(uri);
                var query = uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => pair[0], pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
                var english = uri.Host.StartsWith("en.");
                if (query.GetValueOrDefault("list") == "allpages")
                {
                    Assert.False(english);
                    Assert.Equal(fixture.PortuguesePage + "/Capítulo", query["apprefix"]);
                    if (fixture.MappingMode == "rate-limit") return new(HttpStatusCode.TooManyRequests);
                    if (fixture.MappingMode == "timeout") throw new TaskCanceledException();
                    var pages = new List<object>();
                    for (var order = 1; order <= (fixture.MappingMode == "too-many" ? 65 : fixture.count); order++)
                    {
                        if (fixture.MappingMode == "empty") break;
                        var title = fixture.PortuguesePage + "/Capítulo " + order;
                        if (order == 1) title = fixture.MappingMode switch
                        {
                            "wrong-work" => "Outra obra/Capítulo 1",
                            "unexpected-suffix" => fixture.PortuguesePage + "/Capítulo 1/Análise",
                            "out-of-range" => fixture.PortuguesePage + "/Capítulo 39",
                            _ => title
                        };
                        pages.Add(new { pageid = fixture.MappingMode == "invalid-page-id" ? 0 : 600 + order,
                            ns = fixture.MappingMode == "wrong-namespace" ? 1 : 0, title });
                        if (fixture.MappingMode == "duplicate" && order == 1)
                            pages.Add(new { pageid = 900, ns = 0, title = fixture.PortuguesePage + "/Capítulo1" });
                    }
                    if (fixture.MappingMode == "continue")
                        return fixture.Json(new { @continue = new { apcontinue = "next", @continue = "-||" }, query = new { allpages = pages } });
                    return fixture.Json(new { batchcomplete = true, query = new { allpages = pages } });
                }
                if (query.TryGetValue("page", out var requested) && requested == fixture.page)
                    return fixture.Parse(fixture.Mode == "wrong-index-title" ? "Outra obra" : fixture.page,
                        1000, 100, fixture.Toc(fixture.key == "hp5" ? "Chapters" : "Capítulos", fixture.page, "1"));
                if (query.GetValueOrDefault("oldid") == "1000")
                    return fixture.Parse(fixture.page, fixture.Mode == "changed-index-revision" ? 1001 : 1000,
                        100, text: fixture.IndexHtml());
                if (fixture.Mode == "wait-cancel") await Task.Delay(Timeout.Infinite, token);
                if (fixture.Mode == "timeout") throw new TaskCanceledException();
                if (fixture.Mode == "rate-limit")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new(TimeSpan.FromSeconds(45));
                    return response;
                }
                if (fixture.Mode == "maxlag") return fixture.Json(new { error = new { code = "maxlag" } });
                if (fixture.Mode == "invalid-json") return new(HttpStatusCode.OK) { Content = new StringContent("{") };
                if (fixture.Mode == "invalid-revision-type") return fixture.Json(new { parse = new { title = requested, revid = "invalid", pageid = 201 } });
                if (fixture.Mode == "redirect") return new(HttpStatusCode.Found) { Headers = { Location = new Uri("http://localhost/private") } };
                if (fixture.Mode is "oversized-header" or "oversized-body")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(fixture.Mode == "oversized-body" ? new string('x', 1024 * 1024 + 1) : "{}") };
                    response.Content.Headers.ContentLength = fixture.Mode == "oversized-body" ? null : 1024 * 1024 + 1;
                    return response;
                }
                if (fixture.Mode == "both-missing" || !english && fixture.Mode == "pt-missing")
                    return fixture.Json(new { error = new { code = "missingtitle" } });
                var chapterPage = english ? NarrativeSourceCatalog.Find(fixture.key)!.EnglishPage + "/Chapter 1"
                    : fixture.PortuguesePage + "/Capítulo 1";
                if (query.ContainsKey("page"))
                {
                    var toc = fixture.Toc(english ? "Synopsis" : fixture.Mode == "pt-no-synopsis" ? "Analysis" : "Sinopse",
                        !english && fixture.Mode == "pt-transcluded-synopsis" ? "Outra página" : chapterPage,
                        english ? "4" : "2", !english && fixture.Mode == "pt-wrong-heading-level" ? 3 : 2);
                    if (!english && fixture.Mode == "pt-ambiguous-synopsis")
                        toc = new { sections = new[] { new { hLevel = 2, line = "Sinopse", index = "2", fromTitle = chapterPage }, new { hLevel = 2, line = "Sinopse", index = "4", fromTitle = chapterPage } } };
                    return fixture.Parse(fixture.Mode == "wrong-chapter-title" ? "Outra obra/Capítulo 1" : chapterPage,
                        english ? 3001 : 2001, english ? 301 : 201, toc);
                }
                var html = english ? "<div><h2>Synopsis</h2><p>Ari sees a lantern.</p></div>"
                    : "<div><h2>Sinopse</h2><p>Ari encontra <a href='/wiki/identidade-futura' title='identidade-futura'>Lia</a> &amp; sua carta.<script src='https://localhost/private'>segredo</script><span hidden>segredo</span><span aria-hidden='true'>segredo</span><img alt='segredo' src='https://localhost/private'><span style='display: none'>segredo</span></p><div class='navbox'><p>segredo</p></div><p>A pista parece<br> incerta.<sup>segredo</sup></p></div>";
                if (!english && fixture.Mode == "pt-extra-analysis") html += "<h2>Analysis</h2><p>segredo</p>";
                if (!english && fixture.Mode == "pt-empty-synopsis") html = "<div><h2>Sinopse</h2><p></p></div>";
                return fixture.Parse(chapterPage, fixture.Mode == "changed-chapter-revision" ? 2999 : english ? 3001 : 2001,
                    english ? 301 : 201, text: html);
            }
        }

        public void Dispose() => handler.Dispose();
    }
}
