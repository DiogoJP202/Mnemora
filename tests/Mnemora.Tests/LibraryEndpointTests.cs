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
            new { externalId = "external-demo-1" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await Write(client, HttpMethod.Post, "/api/library/external",
            new { externalId = "external-demo-1" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBook = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book");
        var secondBook = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("book");
        Assert.Equal(firstBook.GetProperty("id").GetGuid(),
            secondBook.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, firstBook.GetProperty("description").ValueKind);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Equal(1, await db.Books.CountAsync());
        Assert.Equal(1, await db.UserBooks.CountAsync());
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
    public async Task Concurrent_external_imports_share_one_book_and_add_it_to_both_libraries()
    {
        using var factory = CreateFactory("concurrent-external", new CoordinatedProvider());
        using var readerA = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var readerB = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Register(readerA);
        await Register(readerB);

        var responses = await Task.WhenAll(
            Write(readerA, HttpMethod.Post, "/api/library/external",
                new { externalId = "external-demo-1" }),
            Write(readerB, HttpMethod.Post, "/api/library/external",
                new { externalId = "external-demo-1" }));
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
        private static readonly ExternalBookMetadata Metadata = new(
            "external-demo-1", "Livro externo", "Autora",
            null, null, null, null, null, "pt-BR");
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([Metadata]);
        public Task<ExternalBookMetadata?> GetAsync(
            string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalBookMetadata?>(
                externalId == Metadata.ExternalId ? Metadata : null);
    }

    private sealed class CoordinatedProvider : IBookMetadataProvider
    {
        private readonly TaskCompletionSource<bool> paired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requests;
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([]);
        public async Task<ExternalBookMetadata?> GetAsync(
            string externalId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref requests) == 2) paired.TrySetResult(true);
            await paired.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return externalId == "external-demo-1"
                ? new ExternalBookMetadata(externalId, "Livro externo", "Autora",
                    null, null, null, null, null, "pt-BR") : null;
        }
    }
}
