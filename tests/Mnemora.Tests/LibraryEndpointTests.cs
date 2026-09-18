using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class LibraryEndpointTests
{
    [Fact]
    public async Task External_selection_imports_metadata_once_without_plot_description()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-external-{Guid.NewGuid():N}.db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                services.RemoveAll<MnemoraDbContext>();
                services.AddDbContext<MnemoraDbContext>(options =>
                    options.UseSqlite($"Data Source={dbFile}"));
                services.RemoveAll<IBookMetadataProvider>();
                services.AddSingleton<IBookMetadataProvider>(new FakeProvider());
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var search = await client.GetStringAsync("/api/books/search?q=externo");
        Assert.Contains("external", search);
        var first = await Write(client, HttpMethod.Post, "/api/library/external",
            new { externalProvider = FakeProvider.Provider, externalId = "external-demo-1" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await Write(client, HttpMethod.Post, "/api/library/external",
            new { externalProvider = FakeProvider.Provider, externalId = "external-demo-1" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBook = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book");
        var secondBook = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book");
        Assert.Equal(firstBook.GetProperty("id").GetGuid(),
            secondBook.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, firstBook.GetProperty("description").ValueKind);
        Assert.Equal("Edição de teste", firstBook.GetProperty("subtitle").GetString());
        Assert.Equal("9780306406157", firstBook.GetProperty("isbn13").GetString());
        Assert.Equal("Editora Exemplo", firstBook.GetProperty("publisher").GetString());
        Assert.Equal("2021-05-01", firstBook.GetProperty("publishedDate").GetString());
        Assert.Equal("pt-BR", firstBook.GetProperty("language").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Equal(1, await db.Books.CountAsync());
        Assert.Equal(1, await db.UserBooks.CountAsync());
        var imported = await db.Books.SingleAsync();
        Assert.Equal(BookCatalogKind.Imported, imported.CatalogKind);
        Assert.Equal(FakeProvider.Provider, imported.ExternalProvider);
        Assert.Equal("Edição de teste", imported.Subtitle);
        Assert.Equal("0306406152", imported.Isbn10);
        Assert.Equal("9780306406157", imported.Isbn13);
        Assert.Equal("Editora Exemplo", imported.Publisher);
        Assert.Equal(new DateOnly(2021, 5, 1), imported.PublishedDate);
        Assert.Equal("pt-BR", imported.Language);
        Assert.Null(imported.OwnerUserId);
    }

    [Fact]
    public async Task Manual_book_requires_authentication_and_rejects_invalid_fields()
    {
        using var factory = CreateFactory("manual-validation", new DisabledProvider());
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        var anonymous = await Write(client, HttpMethod.Post, "/api/library/manual",
            new { title = "Livro particular", author = "Autora", isbn = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await Register(client);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(client, HttpMethod.Post, "/api/library/manual",
                new { title = "   ", author = "Autora", isbn = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(client, HttpMethod.Post, "/api/library/manual",
                new { title = "Livro", author = new string('a', 301), isbn = (string?)null }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(client, HttpMethod.Post, "/api/library/manual",
                new { title = "Livro", author = "Autora", isbn = "1234567890" }))
            .StatusCode);

        var valid = await Write(client, HttpMethod.Post, "/api/library/manual",
            new { title = "Livro sem autoria", author = (string?)null, isbn = (string?)null });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var body = await valid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Autor não informado",
            body.GetProperty("book").GetProperty("author").GetString());
        Assert.False(body.GetProperty("book").GetProperty("memoryPackAvailable").GetBoolean());
    }

    [Fact]
    public async Task Manual_book_is_private_and_equivalent_isbn_reuses_the_owned_book()
    {
        using var factory = CreateFactory("manual-privacy", new DisabledProvider());
        using var owner = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var anotherReader = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var anonymous = factory.CreateClient();
        await Register(owner);
        await Register(anotherReader);

        var first = await Write(owner, HttpMethod.Post, "/api/library/manual", new
        {
            title = "  Livro   particular ", author = " Autora ", isbn = (string?)null
        });
        var second = await Write(owner, HttpMethod.Post, "/api/library/manual", new
        {
            title = "Livro particular", author = "Autora", isbn = "9780306406157"
        });
        var third = await Write(owner, HttpMethod.Post, "/api/library/manual", new
        {
            title = "Livro particular", author = "Autora", isbn = "0-306-40615-2"
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book").GetProperty("id").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book").GetProperty("id").GetGuid();
        var thirdId = (await third.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book").GetProperty("id").GetGuid();
        Assert.Equal(firstId, secondId);
        Assert.Equal(firstId, thirdId);

        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/books/{firstId}")).StatusCode);
        Assert.Contains(firstId.ToString(),
            await owner.GetStringAsync("/api/books/search?q=Livro%20particular"));
        Assert.Contains(firstId.ToString(),
            await owner.GetStringAsync("/api/books/search?q=978-0-306-40615-7"));
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/api/books/{firstId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherReader.GetAsync($"/api/books/{firstId}")).StatusCode);
        Assert.DoesNotContain(firstId.ToString(),
            await anotherReader.GetStringAsync("/api/books/search?q=Livro%20particular"));
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(anotherReader, HttpMethod.Post, $"/api/library/{firstId}"))
            .StatusCode);
        Assert.DoesNotContain("Livro particular", await owner.GetStringAsync("/api/books"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        var book = await db.Books.SingleAsync();
        Assert.Equal(BookCatalogKind.Private, book.CatalogKind);
        Assert.NotNull(book.OwnerUserId);
        Assert.Equal("0306406152", book.Isbn10);
        Assert.Equal("9780306406157", book.Isbn13);
        Assert.Equal(1, await db.UserBooks.CountAsync());
    }

    [Fact]
    public async Task Catalog_and_search_report_pack_availability_without_listing_imports()
    {
        using var factory = CreateFactory("catalog-kinds", new DisabledProvider());
        using var client = factory.CreateClient();
        var ready = new Book { Title = "Memoria pronta", Author = "Autora" };
        var empty = new Book { Title = "Memoria vazia", Author = "Autor" };
        var imported = new Book
        {
            Title = "Memoria importada", Author = "Autor externo",
            CatalogKind = BookCatalogKind.Imported,
            ExternalProvider = "OpenLibrary", ExternalId = "OL1W"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.AddRange(ready, empty, imported);
            await db.SaveChangesAsync();
            db.ReadingUnits.Add(new ReadingUnit
            {
                BookId = ready.Id, Title = "Começo", SafeLabel = "Capítulo 1",
                Slug = "capitulo-1", OrderIndex = 10
            });
            await db.SaveChangesAsync();
        }

        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/books");
        Assert.Equal(2, catalog.GetArrayLength());
        Assert.DoesNotContain(catalog.EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == imported.Id);
        Assert.True(catalog.EnumerateArray().Single(item =>
            item.GetProperty("id").GetGuid() == ready.Id)
            .GetProperty("memoryPackAvailable").GetBoolean());
        Assert.False(catalog.EnumerateArray().Single(item =>
            item.GetProperty("id").GetGuid() == empty.Id)
            .GetProperty("memoryPackAvailable").GetBoolean());

        var search = await client.GetFromJsonAsync<JsonElement>(
            "/api/books/search?q=Memoria");
        Assert.Equal(3, search.GetArrayLength());
        var importedResult = search.EnumerateArray().Single(item =>
            item.GetProperty("id").GetGuid() == imported.Id);
        Assert.Equal("OpenLibrary",
            importedResult.GetProperty("externalProvider").GetString());
        Assert.False(importedResult.GetProperty("memoryPackAvailable").GetBoolean());
        Assert.True(search.EnumerateArray().Single(item =>
            item.GetProperty("id").GetGuid() == ready.Id)
            .GetProperty("memoryPackAvailable").GetBoolean());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/books/{ready.Id}");
        Assert.True(detail.GetProperty("memoryPackAvailable").GetBoolean());
    }

    [Fact]
    public async Task Existing_external_match_is_returned_as_a_local_book()
    {
        using var factory = CreateFactory("known-external", new FakeProvider());
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var imported = new Book
        {
            Title = "Título local sem o termo pesquisado",
            Author = "Autora",
            CatalogKind = BookCatalogKind.Imported,
            ExternalProvider = FakeProvider.Provider,
            ExternalId = "external-demo-1"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(imported);
            await db.SaveChangesAsync();
        }

        var search = await client.GetFromJsonAsync<JsonElement>(
            "/api/books/search?q=isbn-inexistente-localmente");

        var result = Assert.Single(search.EnumerateArray());
        Assert.Equal(imported.Id, result.GetProperty("id").GetGuid());
        Assert.Equal("local", result.GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.Created,
            (await Write(client, HttpMethod.Post, $"/api/library/{imported.Id}")).StatusCode);
    }

    [Fact]
    public async Task Legacy_work_import_is_reused_for_an_edition_result()
    {
        using var factory = CreateFactory("legacy-work-import", new FakeProvider());
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var legacy = new Book
        {
            Title = "Título importado na versão anterior",
            Author = "Autora",
            CatalogKind = BookCatalogKind.Imported,
            ExternalProvider = FakeProvider.Provider,
            ExternalId = FakeProvider.LegacyWorkId
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(legacy);
            await db.SaveChangesAsync();
        }

        var search = await client.GetFromJsonAsync<JsonElement>(
            "/api/books/search?q=resultado-da-edicao");
        var result = Assert.Single(search.EnumerateArray());
        Assert.Equal(legacy.Id, result.GetProperty("id").GetGuid());
        Assert.Equal("local", result.GetProperty("source").GetString());

        var response = await Write(client, HttpMethod.Post, "/api/library/external",
            new { externalProvider = FakeProvider.Provider,
                externalId = FakeProvider.EditionId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBook = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book");
        Assert.Equal(legacy.Id, responseBook.GetProperty("id").GetGuid());
        Assert.Equal("9780306406157", responseBook.GetProperty("isbn13").GetString());

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<MnemoraDbContext>();
        Assert.Equal(1, await verificationDb.Books.CountAsync());
        Assert.Equal(FakeProvider.LegacyWorkId,
            (await verificationDb.Books.SingleAsync()).ExternalId);
    }

    [Fact]
    public async Task Isbn_search_is_canonicalized_for_providers_and_cached()
    {
        var provider = new RecordingProvider();
        using var factory = CreateFactory("isbn-cache", provider);
        using var client = factory.CreateClient();

        var formatted = await client.GetFromJsonAsync<JsonElement>(
            "/api/books/search?q=978-0-306-40615-7");
        var canonical = await client.GetFromJsonAsync<JsonElement>(
            "/api/books/search?q=9780306406157");

        Assert.Equal(1, provider.SearchCalls);
        Assert.Equal("9780306406157", provider.LastQuery);
        var result = Assert.Single(formatted.EnumerateArray());
        Assert.Equal("9780306406157", result.GetProperty("isbn13").GetString());
        Assert.Equal("Edição internacional", result.GetProperty("subtitle").GetString());
        Assert.Equal(320, result.GetProperty("pageCount").GetInt32());
        Assert.Equal("2ª edição", result.GetProperty("edition").GetString());
        Assert.Equal(1, canonical.GetArrayLength());
    }

    [Fact]
    public async Task Library_is_private_and_future_unit_titles_are_neutral()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-library-{Guid.NewGuid():N}.db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                services.RemoveAll<MnemoraDbContext>();
                services.AddDbContext<MnemoraDbContext>(options =>
                    options.UseSqlite($"Data Source={dbFile}"));
            });
        });
        using var readerA = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var readerB = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(readerA);
        await Register(readerB);
        var book = new Book { Title = "Livro de teste", Author = "Equipe Mnemora" };
        var other = new Book { Title = "Outro livro", Author = "Equipe Mnemora" };
        var first = new ReadingUnit
        {
            BookId = book.Id, Title = "A carta", SafeLabel = "Capítulo 1",
            Slug = "capitulo-1", OrderIndex = 10, Type = ReadingUnitType.Chapter
        };
        var future = new ReadingUnit
        {
            BookId = book.Id, Title = "A revelação secreta", SafeLabel = "Capítulo 2",
            Slug = "capitulo-2", OrderIndex = 20, Type = ReadingUnitType.Chapter
        };
        var foreign = new ReadingUnit
        {
            BookId = other.Id, Title = "Outro capítulo", SafeLabel = "Capítulo 1",
            Slug = "outro-1", OrderIndex = 10, Type = ReadingUnitType.Chapter
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.AddRange(book, other);
            await db.SaveChangesAsync();
            db.ReadingUnits.AddRange(first, future, foreign);
            await db.SaveChangesAsync();
        }

        Assert.Contains("Livro de teste", await readerA.GetStringAsync("/api/books"));
        Assert.Contains("Livro de teste", await readerA.GetStringAsync("/api/books/search?q=Livro"));
        Assert.Equal(HttpStatusCode.NotFound,
            (await readerB.GetAsync($"/api/books/{book.Id}/reading-units")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Write(readerA, HttpMethod.Post, $"/api/library/{book.Id}")).StatusCode);
        Assert.Contains("Livro de teste", await readerA.GetStringAsync("/api/library"));
        Assert.Equal("[]", await readerB.GetStringAsync("/api/library"));

        var before = await readerA.GetStringAsync($"/api/books/{book.Id}/reading-units");
        Assert.DoesNotContain("A carta", before);
        Assert.DoesNotContain("A revelação secreta", before);
        Assert.Contains("Capítulo 2", before);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(readerA, HttpMethod.Patch, $"/api/library/{book.Id}/progress",
                new { currentReadingUnitId = foreign.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Write(readerA, HttpMethod.Patch, $"/api/library/{book.Id}/progress",
                new { currentReadingUnitId = first.Id, currentPage = 18 })).StatusCode);
        var after = await readerA.GetStringAsync($"/api/books/{book.Id}/reading-units");
        Assert.Contains("A carta", after);
        Assert.DoesNotContain("A revelação secreta", after);

        Assert.Equal(HttpStatusCode.OK,
            (await Write(readerA, HttpMethod.Patch, $"/api/library/{book.Id}/status",
                new { status = "Finished" })).StatusCode);
        Assert.DoesNotContain("A revelação secreta",
            await readerA.GetStringAsync($"/api/books/{book.Id}/reading-units"));
        Assert.Equal(HttpStatusCode.OK,
            (await Write(readerA, HttpMethod.Patch, $"/api/library/{book.Id}/progress",
                new { currentReadingUnitId = (Guid?)null })).StatusCode);
        Assert.DoesNotContain("A carta",
            await readerA.GetStringAsync($"/api/books/{book.Id}/reading-units"));
        Assert.Equal(HttpStatusCode.NoContent,
            (await Write(readerA, HttpMethod.Delete, $"/api/library/{book.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await readerA.GetAsync($"/api/books/{book.Id}/reading-units")).StatusCode);
    }

    [Fact]
    public async Task Partial_progress_updates_preserve_absent_fields_and_clear_explicit_nulls()
    {
        using var factory = CreateFactory("partial-progress");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var book = new Book { Title = "Progresso parcial", Author = "Equipe Mnemora" };
        var first = new ReadingUnit
        {
            BookId = book.Id, Title = "Primeiro capítulo", SafeLabel = "Capítulo 1",
            Slug = "primeiro", OrderIndex = 10, Type = ReadingUnitType.Chapter
        };
        var second = new ReadingUnit
        {
            BookId = book.Id, Title = "Segundo capítulo", SafeLabel = "Capítulo 2",
            Slug = "segundo", OrderIndex = 20, Type = ReadingUnitType.Chapter
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(book);
            await db.SaveChangesAsync();
            db.ReadingUnits.AddRange(first, second);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created,
            (await Write(client, HttpMethod.Post, $"/api/library/{book.Id}")).StatusCode);
        var path = $"/api/library/{book.Id}/progress";

        await AssertProgress(await Write(client, HttpMethod.Patch, path,
            new { currentReadingUnitId = first.Id, currentPage = 12 }), first.Id, 12);
        await AssertProgress(await Write(client, HttpMethod.Patch, path,
            new { currentPage = 18 }), first.Id, 18);
        Assert.Contains("Primeiro capítulo",
            await client.GetStringAsync($"/api/books/{book.Id}/reading-units"));
        await AssertProgress(await Write(client, HttpMethod.Patch, path,
            new { currentReadingUnitId = second.Id }), second.Id, 18);
        await AssertProgress(await Write(client, HttpMethod.Patch, path,
            new { currentPage = (int?)null }), second.Id, null);
        await AssertProgress(await Write(client, HttpMethod.Patch, path,
            new { currentReadingUnitId = (Guid?)null }), null, null);
        Assert.DoesNotContain("Primeiro capítulo",
            await client.GetStringAsync($"/api/books/{book.Id}/reading-units"));
    }

    [Fact]
    public async Task Numeric_status_string_is_rejected_without_changing_status()
    {
        using var factory = CreateFactory("numeric-status");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var book = new Book { Title = "Status", Author = "Equipe Mnemora" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created,
            (await Write(client, HttpMethod.Post, $"/api/library/{book.Id}")).StatusCode);

        var response = await Write(client, HttpMethod.Patch,
            $"/api/library/{book.Id}/status", new { status = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var library = await client.GetFromJsonAsync<JsonElement>("/api/library");
        Assert.Equal("WantToRead", library[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Page_only_progress_does_not_override_explicit_status()
    {
        using var factory = CreateFactory("page-status", new DisabledProvider());
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);

        var created = await Write(client, HttpMethod.Post, "/api/library/manual", new
        {
            title = "Livro acompanhado por página", author = "Autora", isbn = (string?)null
        });
        var bookId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await Write(client, HttpMethod.Patch, $"/api/library/{bookId}/status",
                new { status = "Finished" })).StatusCode);

        var progress = await Write(client, HttpMethod.Patch,
            $"/api/library/{bookId}/progress", new { currentPage = 321 });

        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        var result = await progress.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Finished", result.GetProperty("status").GetString());
        Assert.Equal(321, result.GetProperty("currentPage").GetInt32());
    }

    [Fact]
    public async Task Manual_book_limit_rejects_only_new_private_books()
    {
        using var factory = CreateFactory("manual-limit", new DisabledProvider());
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var userId = me.GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.AddRange(Enumerable.Range(1, 500).Select(index => new Book
            {
                Title = $"Livro particular {index}",
                Author = "Autora",
                CatalogKind = BookCatalogKind.Private,
                OwnerUserId = userId
            }));
            await db.SaveChangesAsync();
        }

        var response = await Write(client, HttpMethod.Post, "/api/library/manual", new
        {
            title = "Livro além do limite", author = "Autora", isbn = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_external_imports_share_one_book_and_add_it_to_both_libraries()
    {
        using var factory = CreateFactory("concurrent-external", new CoordinatedProvider());
        using var readerA = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var readerB = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(readerA);
        await Register(readerB);

        var responses = await Task.WhenAll(
            Write(readerA, HttpMethod.Post, "/api/library/external",
                new { externalProvider = CoordinatedProvider.Provider,
                    externalId = "external-demo-1" }),
            Write(readerB, HttpMethod.Post, "/api/library/external",
                new { externalProvider = CoordinatedProvider.Provider,
                    externalId = "external-demo-1" }));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var bookIds = await Task.WhenAll(responses.Select(async response =>
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("book").GetProperty("id").GetGuid()));
        Assert.Equal(bookIds[0], bookIds[1]);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Equal(1, await db.Books.CountAsync());
        Assert.Equal(2, await db.UserBooks.CountAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string name, IBookMetadataProvider? provider = null)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-{name}-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                services.RemoveAll<MnemoraDbContext>();
                services.AddDbContext<MnemoraDbContext>(options =>
                    options.UseSqlite($"Data Source={dbFile}"));
                if (provider is not null)
                {
                    services.RemoveAll<IBookMetadataProvider>();
                    services.AddSingleton(provider);
                }
            });
        });
    }

    private static async Task AssertProgress(
        HttpResponseMessage response, Guid? currentUnitId, int? currentPage)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var unit = result.GetProperty("currentReadingUnitId");
        var page = result.GetProperty("currentPage");
        if (currentUnitId is Guid id) Assert.Equal(id, unit.GetGuid());
        else Assert.Equal(JsonValueKind.Null, unit.ValueKind);
        if (currentPage is int number) Assert.Equal(number, page.GetInt32());
        else Assert.Equal(JsonValueKind.Null, page.ValueKind);
    }

    private static async Task Register(HttpClient client)
    {
        var response = await Write(client, HttpMethod.Post, "/api/auth/register", new
        {
            email = $"reader-{Guid.NewGuid():N}@test.local", password = "StrongTestPass123"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Write(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed class FakeProvider : IBookMetadataProvider
    {
        public const string Provider = "TestProvider";
        public const string EditionId = "external-demo-1";
        public const string LegacyWorkId = "external-work-1";
        private static readonly ExternalBookMetadata Metadata = new(
            Provider, EditionId, "Livro externo", "Autora",
            "https://example.test/cover.jpg", "0306406152", "9780306406157",
            "Editora Exemplo", new DateOnly(2021, 5, 1), "pt-BR",
            Subtitle: "Edição de teste", PageCount: 256,
            Categories: ["Ficção"], Edition: "1ª edição", WorkId: LegacyWorkId);
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([Metadata]);
        public Task<ExternalBookMetadata?> GetAsync(
            string provider, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalBookMetadata?>(
                provider == Provider && externalId == Metadata.ExternalId ? Metadata : null);
    }

    private sealed class DisabledProvider : IBookMetadataProvider
    {
        public bool IsConfigured => false;

        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([]);

        public Task<ExternalBookMetadata?> GetAsync(
            string provider, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalBookMetadata?>(null);
    }

    private sealed class RecordingProvider : IBookMetadataProvider
    {
        private static readonly ExternalBookMetadata Metadata = new(
            "RecordingProvider", "recording-1", "Livro internacional", "Autora",
            null, "0306406152", "9780306406157", "Editora", new DateOnly(2020, 1, 1),
            "eng", Subtitle: "Edição internacional", PageCount: 320,
            Categories: ["History"], Edition: "2ª edição");

        public bool IsConfigured => true;
        public int SearchCalls { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken)
        {
            SearchCalls++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([Metadata]);
        }

        public Task<ExternalBookMetadata?> GetAsync(
            string provider, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalBookMetadata?>(null);
    }

    private sealed class CoordinatedProvider : IBookMetadataProvider
    {
        public const string Provider = "CoordinatedTestProvider";
        private readonly TaskCompletionSource<bool> paired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requests;
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([]);
        public async Task<ExternalBookMetadata?> GetAsync(
            string provider, string externalId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref requests) == 2) paired.TrySetResult(true);
            await paired.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return provider == Provider && externalId == "external-demo-1"
                ? new ExternalBookMetadata(Provider, externalId, "Livro externo", "Autora",
                    null, null, null, null, null, "pt-BR") : null;
        }
    }
}
