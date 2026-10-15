using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class BookSourceDiscoveryTests
{
    [Fact]
    public async Task Discovery_requires_admin_csrf_and_shared_book()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Write(anonymous, fixture.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(fixture.Reader, fixture.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Admin.PostAsync(fixture.Path, null)).StatusCode);
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync();
            book.CatalogKind = BookCatalogKind.Private;
            book.OwnerUserId = await db.Users.Select(user => user.Id).FirstAsync();
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await Write(fixture.Admin, fixture.Path)).StatusCode);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Discovery_uses_server_identity_filters_adaptations_and_never_creates_editorial_data()
    {
        using var fixture = await Fixture.CreateAsync();
        var response = await Write(fixture.Admin, fixture.Path, new { title = "http://localhost:9999/secret", cursor = 99 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var result = await response.Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.NotNull(result);
        Assert.Equal("Livro das Marés", result.Book.Title);
        Assert.Equal("Candidate", result.OutlineStatus);
        Assert.Single(result.Outline);
        Assert.Equal("NotVerified", result.NarrativeStatus);
        Assert.Equal(4, result.Sources.Count);
        Assert.All(result.Sources, source =>
        {
            Assert.True(source.RequiresReview);
            Assert.Matches("^[a-f0-9]{64}$", source.ResponseHash);
            Assert.Contains(new Uri(source.Url).Host, new[] { "openlibrary.org", "www.wikidata.org", "pt.wikipedia.org", "en.wikipedia.org" });
        });
        Assert.All(result.Sources.Where(source => source.Provider != "OpenLibrary"), source => Assert.Equal("WorkIdentifier", source.Match));
        Assert.DoesNotContain("future spoiler", await response.Content.ReadAsStringAsync());
        Assert.Contains(result.Diagnostics, message => message.Contains("descartado"));
        Assert.Equal(3, fixture.Handler.Calls);
        Assert.Contains(fixture.Handler.Requests, path => path.Contains("search=Livro%20das%20Mar"));
        await fixture.WithDb(async db =>
        {
            Assert.Single(await db.Books.ToListAsync());
            Assert.Empty(await db.ReadingUnits.ToListAsync());
            Assert.Empty(await db.LoreEntities.ToListAsync());
            Assert.Empty(await db.UserBooks.ToListAsync());
            Assert.Equal(EditorialContentStatus.None, (await db.Books.SingleAsync()).LoreStatus);
        });
    }

    [Fact]
    public async Task Cache_is_reused_but_changed_bibliographic_identity_gets_a_new_lookup()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await (await Write(fixture.Admin, fixture.Path)).Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        var second = await (await Write(fixture.Admin, fixture.Path)).Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.Equal(first!.RetrievedAt, second!.RetrievedAt);
        Assert.Equal(3, fixture.Handler.Calls);
        await fixture.WithDb(async db => { (await db.Books.SingleAsync()).Title = "Edição revisada"; await db.SaveChangesAsync(); });
        var changed = await (await Write(fixture.Admin, fixture.Path)).Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.Equal("Edição revisada", changed!.Book.Title);
        Assert.Equal(6, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("missing", "Missing")]
    [InlineData("empty", "Missing")]
    [InlineData("conflict", "Conflict")]
    [InlineData("invalid", "Failed")]
    [InlineData("oversized-header", "Failed")]
    [InlineData("oversized-body", "Failed")]
    [InlineData("redirect", "Failed")]
    [InlineData("rate-limit", "Failed")]
    [InlineData("timeout", "Failed")]
    [InlineData("too-many-units", "Failed")]
    public async Task Outline_problems_preserve_references_without_inventing_chapters(string mode, string expected)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Handler.OutlineMode = mode;
        var response = await Write(fixture.Admin, fixture.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.Equal(expected, result!.OutlineStatus);
        Assert.Empty(result.Outline);
        Assert.Contains(result.Sources, source => source.Provider == "Wikidata");
        Assert.DoesNotContain("provider-secret", await response.Content.ReadAsStringAsync());
        Assert.Equal(3, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("rate-limit")]
    [InlineData("maxlag")]
    [InlineData("empty")]
    [InlineData("malformed-claims")]
    public async Task Reference_failures_preserve_outline_and_show_gaps(string mode)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Handler.ReferenceMode = mode;
        var response = await Write(fixture.Admin, fixture.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.Equal("Candidate", result!.OutlineStatus);
        Assert.Single(result.Outline);
        Assert.Equal("NotVerified", result.NarrativeStatus);
        Assert.DoesNotContain(result.Sources, source => source.Provider == "Wikipedia");
        Assert.True(result.Diagnostics.Count >= 2);
    }

    [Fact]
    public async Task Other_providers_never_borrow_an_unrelated_edition_outline()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db => { (await db.Books.SingleAsync()).ExternalProvider = "GoogleBooks"; await db.SaveChangesAsync(); });
        var result = await (await Write(fixture.Admin, fixture.Path)).Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.Equal("NotApplicable", result!.OutlineStatus);
        Assert.Empty(result.Outline);
        Assert.Equal(2, fixture.Handler.Calls);
        Assert.All(result.Sources, source => Assert.Equal("UnverifiedWork", source.Match));
        Assert.Contains(result.Diagnostics, message => message.Contains("sem identificador"));
    }

    [Fact]
    public async Task Isbn_match_is_distinct_from_a_work_identifier_and_still_requires_review()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Handler.IncludeIsbn = true;
        var result = await (await Write(fixture.Admin, fixture.Path)).Content.ReadFromJsonAsync<BookSourceDiscoveryResult>();
        Assert.All(result!.Sources.Where(source => source.Provider != "OpenLibrary"), source =>
        { Assert.Equal("EditionIsbn", source.Match); Assert.True(source.RequiresReview); });
    }

    [Fact]
    public async Task Concurrent_lookup_returns_429_and_edited_identity_is_rejected_before_delivery()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Handler.BlockSearch = true;
        var first = Write(fixture.Admin, fixture.Path);
        await fixture.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var concurrent = await Write(fixture.Admin, fixture.Path);
        Assert.Equal(HttpStatusCode.TooManyRequests, concurrent.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(15), concurrent.Headers.RetryAfter!.Delta);
        await fixture.WithDb(async db => { (await db.Books.SingleAsync()).Author = "Outra autoria"; await db.SaveChangesAsync(); });
        fixture.Handler.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.Conflict, (await first).StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_releases_global_gate_and_does_not_cache_partial_result()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1_000 });
        using var handler = new FakeHandler { BlockSearch = true };
        var service = new BookSourceDiscovery(name => Client(name, handler), cache, TimeProvider.System);
        using var cancel = new CancellationTokenSource();
        var first = service.DiscoverAsync(Identity(Guid.NewGuid()), cancel.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<SourceDiscoveryBusyException>(() => service.DiscoverAsync(Identity(Guid.NewGuid()), CancellationToken.None));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.BlockSearch = false;
        var result = await service.DiscoverAsync(Identity(Guid.NewGuid()), CancellationToken.None);
        Assert.Equal("Candidate", result.OutlineStatus);
        Assert.Equal(5, handler.Calls);
    }

    private static SourceBookIdentity Identity(Guid id) => new(id, "Livro das Marés", "Autora original", "0306406152", "9780306406157", "pt-BR", null, null, "OpenLibrary", "OL123M");
    private static HttpClient Client(string name, FakeHandler handler) => new(handler, disposeHandler: false)
    { BaseAddress = new Uri(name == "discovery-openlibrary" ? "https://openlibrary.org/" : "https://www.wikidata.org/") };
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Reader { get; init; }
        public required FakeHandler Handler { get; init; }
        public Guid BookId { get; init; }
        public string Path => $"/api/admin/books/{BookId}/source-discovery";
        public static async Task<Fixture> CreateAsync()
        {
            var database = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mnemora-discovery-{Guid.NewGuid():N}.db");
            var handler = new FakeHandler();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                    services.RemoveAll<MnemoraDbContext>();
                    services.AddDbContext<MnemoraDbContext>(options => options.UseSqlite($"Data Source={database};Pooling=False"));
                    services.RemoveAll<BookSourceDiscovery>();
                    services.AddSingleton(provider => new BookSourceDiscovery(name => Client(name, handler),
                        provider.GetRequiredService<IMemoryCache>(), TimeProvider.System));
                });
            });
            var admin = factory.CreateClient(); var reader = factory.CreateClient();
            var bookId = Guid.NewGuid();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
                Assert.True((await roles.CreateAsync(new("Admin"))).Succeeded);
                foreach (var name in new[] { "admin", "reader" })
                {
                    var user = new IdentityUser<Guid> { UserName = $"{name}@discovery.test", Email = $"{name}@discovery.test" };
                    Assert.True((await users.CreateAsync(user, "StrongDiscovery123")).Succeeded);
                    if (name == "admin") Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                }
                var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
                db.Books.Add(new Book { Id = bookId, Title = "Livro das Marés", Author = "Autora original",
                    ExternalProvider = "OpenLibrary", ExternalId = "OL123M", Isbn13 = "9780306406157", Isbn10 = "0306406152",
                    Language = "pt-BR", CatalogKind = BookCatalogKind.Imported,
                    ReadingOutlineStatus = EditorialContentStatus.None, LoreStatus = EditorialContentStatus.None });
                await db.SaveChangesAsync();
            }
            foreach (var pair in new[] { (admin, "admin"), (reader, "reader") })
                Assert.Equal(HttpStatusCode.OK, (await Write(pair.Item1, "/api/auth/login", new
                { email = $"{pair.Item2}@discovery.test", password = "StrongDiscovery123" })).StatusCode);
            return new() { Factory = factory, Admin = admin, Reader = reader, Handler = handler, BookId = bookId };
        }
        public async Task WithDb(Func<MnemoraDbContext, Task> action)
        { await using var scope = Factory.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<MnemoraDbContext>()); }
        public void Dispose() { Admin.Dispose(); Reader.Dispose(); Factory.Dispose(); Handler.Dispose(); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        public List<string> Requests { get; } = [];
        public string OutlineMode { get; set; } = "normal";
        public string ReferenceMode { get; set; } = "normal";
        public bool IncludeIsbn { get; set; }
        public bool BlockSearch { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            var uri = request.RequestUri!;
            lock (Requests) Requests.Add(uri.AbsoluteUri);
            Assert.Contains(uri.Host, new[] { "openlibrary.org", "www.wikidata.org" });
            var outline = uri.Host == "openlibrary.org";
            var mode = outline ? OutlineMode : ReferenceMode;
            if (mode == "rate-limit") return new(HttpStatusCode.TooManyRequests);
            if (mode == "redirect") return new(HttpStatusCode.Redirect) { Headers = { Location = new("http://127.0.0.1/secret") } };
            if (mode == "missing") return new(HttpStatusCode.NotFound);
            if (mode == "timeout") throw new TaskCanceledException("provider-secret");
            if (mode == "invalid") return Json("not JSON provider-secret");
            if (mode == "maxlag") return Json("{\"error\":{\"code\":\"maxlag\"}}");
            if (mode == "oversized-header")
            { var huge = Json("{}"); huge.Content.Headers.ContentLength = 1_048_577; return huge; }
            if (mode == "oversized-body") return new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[1_048_577])) };
            if (outline)
            {
                var json = Outline;
                if (mode == "conflict") json = json.Replace("9780306406157", "9788532530790");
                if (mode == "empty") json = json.Replace("[{\"title\":\"A chegada\"}]", "[]");
                if (mode == "too-many-units") json = json.Replace("[{\"title\":\"A chegada\"}]",
                    JsonSerializer.Serialize(Enumerable.Range(1, 201).Select(index => new { title = $"Capítulo {index}" })));
                return Json(json);
            }
            if (uri.Query.Contains("wbsearchentities"))
            {
                if (BlockSearch) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
                return Json(mode == "empty" ? "{\"search\":[]}" : "{\"search\":[{\"id\":\"Q1\"},{\"id\":\"Q2\"},{\"id\":\"http://127.0.0.1/secret\"}]}");
            }
            var entities = Entities;
            if (mode == "malformed-claims") entities = entities.Replace("[{\"mainsnak\":{\"datavalue\":{\"value\":{\"id\":\"Q7725634\"}}}}]", "[null,5,{}]");
            if (IncludeIsbn) entities = entities.Replace("\"P648\":", "\"P212\":[{\"mainsnak\":{\"datavalue\":{\"value\":\"9780306406157\"}}}],\"P648\":");
            return Json(entities);
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        private const string Outline = """
            {"key":"/books/OL123M","isbn_13":["9780306406157"],"works":[{"key":"/works/OL123W"}],"table_of_contents":[{"title":"A chegada"}]}
            """;
        private const string Entities = """
            {"entities":{
              "Q1":{"id":"Q1","labels":{"pt":{"value":"Livro das Marés"}},"descriptions":{"pt":{"value":"future spoiler"}},"claims":{
                "P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7725634"}}}}],
                "P648":[{"mainsnak":{"datavalue":{"value":"OL123W"}}}]},
                "sitelinks":{"ptwiki":{"title":"Livro das Marés"},"enwiki":{"title":"Tide Book"}}},
              "Q2":{"id":"Q2","labels":{"pt":{"value":"Filme"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q11424"}}}}]}}
            }}
            """;
    }
}
