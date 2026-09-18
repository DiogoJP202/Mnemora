using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class BookMetadataProviderTests
{
    [Fact]
    public async Task Open_Library_search_maps_safe_metadata_and_work_identifier()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("openlibrary.org", request.RequestUri?.Host);
            Assert.Contains("search.json", request.RequestUri?.AbsolutePath);
            Assert.Contains("lang=pt", request.RequestUri?.Query);
            return Json("""
                {
                  "docs": [
                    {
                      "key": "/works/OL123W",
                      "title": "A Cidade Invisível",
                      "author_name": ["Ana Luz", "Ana Luz"],
                      "cover_i": 456,
                      "first_publish_year": 2004,
                      "language": ["por"]
                    },
                    { "key": "/authors/OL9A", "title": "Inválido" }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };
        var provider = new OpenLibraryProvider(client);

        var result = Assert.Single(await provider.SearchAsync(
            "cidade invisível", CancellationToken.None));

        Assert.Equal(BookMetadataProviders.OpenLibrary, result.Provider);
        Assert.Equal("OL123W", result.ExternalId);
        Assert.Equal("A Cidade Invisível", result.Title);
        Assert.Equal("Ana Luz", result.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/456-M.jpg?default=false",
            result.CoverUrl);
        Assert.Equal(new DateOnly(2004, 1, 1), result.PublishedDate);
        Assert.Equal("por", result.Language);
        Assert.Null(result.Isbn10);
        Assert.Null(result.Isbn13);
        Assert.Null(result.Publisher);
    }

    [Fact]
    public async Task Open_Library_get_rejects_invalid_ids_without_a_request()
    {
        var handler = new StubHttpHandler(_ =>
            throw new InvalidOperationException("HTTP should not be called."));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };
        var provider = new OpenLibraryProvider(client);

        Assert.Null(await provider.GetAsync("../books/secret", CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Open_Library_get_normalizes_a_work_id_and_returns_the_matching_work()
    {
        var handler = new StubHttpHandler(request =>
        {
            var decodedQuery = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("q=key:/works/OL123W", decodedQuery);
            Assert.Contains("limit=1", decodedQuery);
            return Json("""
                {
                  "docs": [
                    {
                      "key": "/works/OL123W",
                      "title": "A Cidade Invisível",
                      "author_name": ["Ana Luz"]
                    }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };
        var provider = new OpenLibraryProvider(client);

        var result = await provider.GetAsync("/works/ol123w", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("OL123W", result.ExternalId);
        Assert.Equal("A Cidade Invisível", result.Title);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Open_Library_get_returns_null_when_the_work_is_absent()
    {
        var handler = new StubHttpHandler(_ => Json("{\"docs\":[]}"));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };

        var result = await new OpenLibraryProvider(client)
            .GetAsync("OL999W", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Composite_keeps_a_working_source_and_routes_get_by_provider()
    {
        var google = new FakeSource(BookMetadataProviders.GoogleBooks,
            searchError: new HttpRequestException("Google unavailable"));
        var openBook = Metadata(BookMetadataProviders.OpenLibrary, "OL123W");
        var open = new FakeSource(BookMetadataProviders.OpenLibrary, [openBook]);
        var provider = new CompositeBookMetadataProvider(
            [google, open], NullLogger<CompositeBookMetadataProvider>.Instance);

        var search = await provider.SearchAsync("cidade", CancellationToken.None);
        Assert.Equal(openBook, Assert.Single(search));
        Assert.Equal(openBook, await provider.GetAsync(
            "openlibrary", "OL123W", CancellationToken.None));
        Assert.Equal(0, google.GetCalls);
        Assert.Equal(1, open.GetCalls);
        Assert.Null(await provider.GetAsync("Unknown", "OL123W", CancellationToken.None));
    }

    [Fact]
    public async Task Composite_removes_cross_provider_title_and_author_duplicates()
    {
        var googleBook = Metadata(BookMetadataProviders.GoogleBooks, "google-1");
        var openDuplicate = Metadata(BookMetadataProviders.OpenLibrary, "OL123W");
        var openUnique = Metadata(BookMetadataProviders.OpenLibrary, "OL456W")
            with { Title = "Outro livro" };
        var provider = new CompositeBookMetadataProvider(
            [
                new FakeSource(BookMetadataProviders.GoogleBooks, [googleBook]),
                new FakeSource(BookMetadataProviders.OpenLibrary,
                    [openDuplicate, openUnique])
            ],
            NullLogger<CompositeBookMetadataProvider>.Instance);

        var result = await provider.SearchAsync("livro", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(googleBook, result[0]);
        Assert.Equal(openUnique, result[1]);
    }

    private static ExternalBookMetadata Metadata(string provider, string id) =>
        new(provider, id, "Livro", "Autora", null,
            null, null, null, null, "pt-BR");

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class FakeSource(
        string provider,
        IReadOnlyList<ExternalBookMetadata>? searchResults = null,
        Exception? searchError = null) : IBookMetadataSource
    {
        public string Provider => provider;
        public bool IsConfigured => true;
        public int GetCalls { get; private set; }

        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            searchError is null
                ? Task.FromResult(searchResults ?? [])
                : Task.FromException<IReadOnlyList<ExternalBookMetadata>>(searchError);

        public Task<ExternalBookMetadata?> GetAsync(
            string externalId, CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(searchResults?
                .FirstOrDefault(item => item.ExternalId == externalId));
        }
    }
}
