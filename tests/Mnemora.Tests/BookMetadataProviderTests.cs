using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class BookMetadataProviderTests
{
    [Fact]
    public async Task Google_Books_maps_rich_metadata_and_chooses_the_best_https_cover()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("www.googleapis.com", request.RequestUri?.Host);
            return Json("""
                {
                  "items": [
                    {
                      "id": "volume-1",
                      "volumeInfo": {
                        "title": "Fantastic Mr. Fox",
                        "subtitle": "A novel",
                        "authors": ["Roald Dahl"],
                        "publisher": "Puffin",
                        "publishedDate": "1988-10",
                        "description": "This must never be imported.",
                        "industryIdentifiers": [
                          { "type": "ISBN_10", "identifier": "0-14-032872-6" },
                          { "type": "ISBN_13", "identifier": "9780140328721" }
                        ],
                        "pageCount": 96,
                        "categories": ["Juvenile Fiction", "Fantasy"],
                        "imageLinks": {
                          "smallThumbnail": "http://images.example/small-thumb.jpg",
                          "thumbnail": "http://images.example/thumb.jpg",
                          "large": "http://images.example/large.jpg"
                        },
                        "language": "en"
                      }
                    }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler);
        var provider = new GoogleBooksProvider(client, GoogleConfiguration());

        var result = Assert.Single(await provider.SearchAsync(
            "Fantastic Mr. Fox", CancellationToken.None));

        Assert.Equal(BookMetadataProviders.GoogleBooks, result.Provider);
        Assert.Equal("volume-1", result.ExternalId);
        Assert.Equal("Fantastic Mr. Fox", result.Title);
        Assert.Equal("A novel", result.Subtitle);
        Assert.Equal("Roald Dahl", result.Author);
        Assert.Equal("https://images.example/large.jpg", result.CoverUrl);
        Assert.Equal("0140328726", result.Isbn10);
        Assert.Equal("9780140328721", result.Isbn13);
        Assert.Equal("Puffin", result.Publisher);
        Assert.Equal(new DateOnly(1988, 10, 1), result.PublishedDate);
        Assert.Equal("en", result.Language);
        Assert.Equal(96, result.PageCount);
        Assert.Equal(["Juvenile Fiction", "Fantasy"], result.Categories);
    }

    [Fact]
    public async Task Google_Books_favors_a_normalized_isbn_query()
    {
        var handler = new StubHttpHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("q=isbn:9780140328721", query);
            return Json("{\"items\":[]}");
        });
        using var client = new HttpClient(handler);
        var provider = new GoogleBooksProvider(client, GoogleConfiguration());

        await provider.SearchAsync("978-0-140-32872-1", CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Google_Books_ignores_malformed_values_without_losing_valid_items()
    {
        var handler = new StubHttpHandler(_ => Json("""
            {
              "items": [
                42,
                { "id": 7, "volumeInfo": { "title": "Inválido" } },
                { "id": "bad-title", "volumeInfo": { "title": [] } },
                {
                  "id": "valid",
                  "volumeInfo": {
                    "title": "Livro válido",
                    "authors": [42],
                    "publishedDate": 2020,
                    "industryIdentifiers": [null, { "type": 2, "identifier": [] }]
                  }
                }
              ]
            }
            """));
        using var client = new HttpClient(handler);
        var provider = new GoogleBooksProvider(client, GoogleConfiguration());

        var result = Assert.Single(await provider.SearchAsync(
            "livro", CancellationToken.None));

        Assert.Equal("valid", result.ExternalId);
        Assert.Equal("Autor não informado", result.Author);
        Assert.Null(result.PublishedDate);
        Assert.Null(result.Isbn13);
    }

    [Fact]
    public async Task Open_Library_search_uses_the_matching_edition_metadata()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("openlibrary.org", request.RequestUri?.Host);
            var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("editions.isbn", query);
            Assert.Contains("editions.publisher", query);
            return Json("""
                {
                  "docs": [
                    {
                      "key": "/works/OL18016709W",
                      "title": "Fire & Blood",
                      "author_name": ["George R. R. Martin", "George R. R. Martin"],
                      "cover_i": 12063529,
                      "first_publish_year": 2014,
                      "language": ["eng", "por"],
                      "isbn": ["9781984838698"],
                      "publisher": ["Bantam", "Suma"],
                      "number_of_pages_median": 840,
                      "subject": ["Fantasy", "Dragons", "Fantasy"],
                      "editions": {
                        "docs": [
                          {
                            "key": "/books/OL28645793M",
                            "title": "Fogo & Sangue",
                            "subtitle": "Volume 1",
                            "cover_i": 10328380,
                            "language": ["por"],
                            "publisher": ["Suma"],
                            "publish_date": ["Aug 05, 2018"],
                            "isbn": ["9788556510761", "8556510760"],
                            "number_of_pages": 736,
                            "edition_name": "1ª edição"
                          }
                        ]
                      }
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

        var result = Assert.Single(await provider.SearchAsync(
            "fogo e sangue", CancellationToken.None));

        Assert.Equal(BookMetadataProviders.OpenLibrary, result.Provider);
        Assert.Equal("OL28645793M", result.ExternalId);
        Assert.Equal("OL18016709W", result.WorkId);
        Assert.Equal("Fogo & Sangue", result.Title);
        Assert.Equal("Volume 1", result.Subtitle);
        Assert.Equal("George R. R. Martin", result.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/10328380-M.jpg?default=false",
            result.CoverUrl);
        Assert.Equal("8556510760", result.Isbn10);
        Assert.Equal("9788556510761", result.Isbn13);
        Assert.Equal("Suma", result.Publisher);
        Assert.Equal(new DateOnly(2018, 8, 5), result.PublishedDate);
        Assert.Equal("por", result.Language);
        Assert.Equal(736, result.PageCount);
        Assert.Equal(["Fantasy", "Dragons"], result.Categories);
        Assert.Equal("1ª edição", result.Edition);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Open_Library_search_falls_back_to_safe_work_metadata()
    {
        var handler = new StubHttpHandler(_ => Json("""
            {
              "docs": [
                {
                  "key": "/works/OL123W",
                  "title": "A Cidade Invisível",
                  "author_name": ["Ana Luz", "Ana Luz"],
                  "cover_i": 456,
                  "first_publish_year": 2004,
                  "language": ["por"],
                  "number_of_pages_median": 240,
                  "publisher": ["Editora Exemplo"]
                },
                { "key": "/authors/OL9A", "title": "Inválido" }
              ]
            }
            """));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };
        var provider = new OpenLibraryProvider(client);

        var result = Assert.Single(await provider.SearchAsync(
            "cidade invisível", CancellationToken.None));

        Assert.Equal("OL123W", result.ExternalId);
        Assert.Equal("A Cidade Invisível", result.Title);
        Assert.Equal("Ana Luz", result.Author);
        Assert.Equal("Editora Exemplo", result.Publisher);
        Assert.Equal(new DateOnly(2004, 1, 1), result.PublishedDate);
        Assert.Equal("por", result.Language);
        Assert.Equal(240, result.PageCount);
        Assert.Null(result.Isbn10);
        Assert.Null(result.Isbn13);
    }

    [Fact]
    public async Task Open_Library_favors_a_normalized_isbn_query()
    {
        var handler = new StubHttpHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("isbn=9780140328721", query);
            Assert.DoesNotContain("q=978", query);
            return Json("{\"docs\":[]}");
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };

        await new OpenLibraryProvider(client).SearchAsync(
            "978-0-140-32872-1", CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Open_Library_get_supports_an_edition_identifier()
    {
        var handler = new StubHttpHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("q=edition_key:OL28645793M", query);
            Assert.Contains("limit=1", query);
            return Json("""
                {
                  "docs": [
                    {
                      "key": "/works/OL18016709W",
                      "title": "Fire & Blood",
                      "author_name": ["George R. R. Martin"],
                      "editions": {
                        "docs": [
                          {
                            "key": "/books/OL28645793M",
                            "title": "Fogo & Sangue",
                            "isbn": ["9788556510761"]
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };

        var result = await new OpenLibraryProvider(client)
            .GetAsync("/books/ol28645793m", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("OL28645793M", result.ExternalId);
        Assert.Equal("OL18016709W", result.WorkId);
        Assert.Equal("9788556510761", result.Isbn13);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Open_Library_get_preserves_a_legacy_work_identifier()
    {
        var handler = new StubHttpHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("q=key:/works/OL123W", query);
            return Json("""
                {
                  "docs": [
                    {
                      "key": "/works/OL123W",
                      "title": "A Cidade Invisível",
                      "author_name": ["Ana Luz"],
                      "editions": {
                        "docs": [
                          { "key": "/books/OL456M", "title": "A Cidade Invisível" }
                        ]
                      }
                    }
                  ]
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openlibrary.org/")
        };

        var result = await new OpenLibraryProvider(client)
            .GetAsync("/works/ol123w", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("OL123W", result.ExternalId);
        Assert.Equal("OL123W", result.WorkId);
        Assert.Equal(1, handler.RequestCount);
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
    public async Task Open_Library_get_returns_null_when_the_book_is_absent()
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
    public async Task Composite_keeps_Open_Library_when_Google_Books_fails()
    {
        var google = new FakeSource(BookMetadataProviders.GoogleBooks,
            searchError: new HttpRequestException("Google unavailable"));
        var openBook = Metadata(BookMetadataProviders.OpenLibrary, "OL123W");
        var open = new FakeSource(BookMetadataProviders.OpenLibrary, [openBook]);
        var provider = Composite(google, open);

        var search = await provider.SearchAsync("cidade", CancellationToken.None);

        Assert.Equal(openBook, Assert.Single(search));
        Assert.Equal(openBook, await provider.GetAsync(
            "openlibrary", "OL123W", CancellationToken.None));
        Assert.Equal(0, google.GetCalls);
        Assert.Equal(1, open.GetCalls);
    }

    [Fact]
    public async Task Composite_keeps_Google_Books_when_Open_Library_fails()
    {
        var googleBook = Metadata(BookMetadataProviders.GoogleBooks, "google-1");
        var google = new FakeSource(BookMetadataProviders.GoogleBooks, [googleBook]);
        var open = new FakeSource(BookMetadataProviders.OpenLibrary,
            searchError: new HttpRequestException("Open Library unavailable"));
        var provider = Composite(google, open);

        var search = await provider.SearchAsync("cidade", CancellationToken.None);

        Assert.Equal(googleBook, Assert.Single(search));
    }

    [Fact]
    public async Task Composite_deduplicates_cross_provider_results_by_isbn()
    {
        var googleBook = Metadata(BookMetadataProviders.GoogleBooks, "google-1")
            with
        { Isbn13 = "9780140328721", Isbn10 = "0140328726" };
        var openDuplicate = Metadata(BookMetadataProviders.OpenLibrary, "OL456M")
            with
        { Title = "Fantastic Mr. Fox", Isbn10 = "0-14-032872-6" };
        var provider = Composite(
            new FakeSource(BookMetadataProviders.GoogleBooks, [googleBook]),
            new FakeSource(BookMetadataProviders.OpenLibrary, [openDuplicate]));

        var result = await provider.SearchAsync("livro", CancellationToken.None);

        Assert.Equal(googleBook, Assert.Single(result));
    }

    [Fact]
    public async Task Composite_preserves_editions_with_the_same_title_and_author()
    {
        var firstEdition = Metadata(BookMetadataProviders.GoogleBooks, "google-1")
            with
        { Isbn13 = "9780140328721" };
        var secondEdition = Metadata(BookMetadataProviders.OpenLibrary, "OL456M")
            with
        { Isbn13 = "9788556510761" };
        var noIsbnEdition = Metadata(BookMetadataProviders.OpenLibrary, "OL789M");
        var provider = Composite(
            new FakeSource(BookMetadataProviders.GoogleBooks, [firstEdition]),
            new FakeSource(BookMetadataProviders.OpenLibrary,
                [secondEdition, noIsbnEdition]));

        var result = await provider.SearchAsync("livro", CancellationToken.None);

        Assert.Equal([firstEdition, secondEdition, noIsbnEdition], result);
    }

    [Fact]
    public async Task Composite_deduplicates_a_repeated_provider_identifier_without_isbn()
    {
        var book = Metadata(BookMetadataProviders.OpenLibrary, "OL456M");
        var provider = Composite(new FakeSource(
            BookMetadataProviders.OpenLibrary, [book, book]));

        var result = await provider.SearchAsync("livro", CancellationToken.None);

        Assert.Equal(book, Assert.Single(result));
        Assert.Null(await provider.GetAsync(
            "Unknown", "OL456M", CancellationToken.None));
    }

    [Fact]
    public async Task Composite_does_not_reserve_an_external_id_for_a_rejected_isbn_duplicate()
    {
        var accepted = Metadata(BookMetadataProviders.GoogleBooks, "google-1")
            with
        { Isbn13 = "9780140328721" };
        var rejected = Metadata(BookMetadataProviders.OpenLibrary, "OL456M")
            with
        { Isbn13 = "9780140328721" };
        var laterValid = Metadata(BookMetadataProviders.OpenLibrary, "OL456M")
            with
        { Isbn13 = "9788556510761" };
        var provider = Composite(
            new FakeSource(BookMetadataProviders.GoogleBooks, [accepted]),
            new FakeSource(BookMetadataProviders.OpenLibrary, [rejected, laterValid]));

        var result = await provider.SearchAsync("livro", CancellationToken.None);

        Assert.Equal([accepted, laterValid], result);
    }

    private static CompositeBookMetadataProvider Composite(
        params IBookMetadataSource[] sources) => new(
            sources, NullLogger<CompositeBookMetadataProvider>.Instance);

    private static ExternalBookMetadata Metadata(string provider, string id) =>
        new(provider, id, "Livro", "Autora", null,
            null, null, null, null, "pt-BR");

    private static IConfiguration GoogleConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["GOOGLE_BOOKS_API_KEY"] = "test-key"
            }).Build();

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
