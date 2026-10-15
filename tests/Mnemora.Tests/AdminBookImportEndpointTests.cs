using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class AdminBookImportEndpointTests
{
    [Fact]
    public async Task Search_and_import_require_admin_and_writes_require_csrf()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Search)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Write(anonymous, Import, Reference)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Reader.GetAsync(Search)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(fixture.Reader, Import, Reference)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await fixture.Admin.PostAsJsonAsync(Import, Reference)).StatusCode);
        await fixture.WithDb(async db => Assert.Empty(await db.Books.ToListAsync()));
    }

    [Fact]
    public async Task Admin_search_is_read_only_cached_and_excludes_even_the_admins_private_books()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            db.Books.AddRange(
                new Book { Title = "Livro público", Author = "Autora" },
                new Book { Title = "Livro privado do Admin", Author = "Autora",
                    CatalogKind = BookCatalogKind.Private, OwnerUserId = fixture.AdminId },
                new Book { Title = "Livro privado do leitor", Author = "Autora",
                    CatalogKind = BookCatalogKind.Private, OwnerUserId = fixture.ReaderId });
            await db.SaveChangesAsync();
        });
        var first = await fixture.Admin.GetAsync("/api/admin/books/search?q=978-0-306-40615-7");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        await fixture.Admin.GetAsync("/api/admin/books/search?q=9780306406157");
        Assert.Equal(1, fixture.Provider.SearchCalls);
        Assert.Equal("9780306406157", fixture.Provider.LastQuery);
        var search = await fixture.Admin.GetStringAsync(Search);
        Assert.Contains("Livro público", search);
        Assert.DoesNotContain("Livro privado", search);
        Assert.DoesNotContain("spoiler", search, StringComparison.OrdinalIgnoreCase);
        await fixture.WithDb(async db =>
        {
            Assert.Equal(3, await db.Books.CountAsync());
            Assert.Empty(await db.UserBooks.ToListAsync());
        });
        fixture.Provider.Fail = true;
        var fallback = await fixture.Admin.GetAsync("/api/admin/books/search?q=Livro%20público");
        Assert.Equal(HttpStatusCode.OK, fallback.StatusCode);
        Assert.Contains("Livro público", await fallback.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_import_uses_server_metadata_once_and_does_not_create_personal_or_editorial_data()
    {
        using var fixture = await Fixture.CreateAsync();
        var forged = new { externalProvider = "Fixture", externalId = "edition-1",
            title = "Client spoiler", author = "Client author", description = "Client plot" };
        var first = await Write(fixture.Admin, Import, forged);
        var second = await Write(fixture.Admin, Import, Reference);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        var book = await first.Content.ReadFromJsonAsync<JsonElement>();
        var repeated = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(book.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal("Livro externo", book.GetProperty("title").GetString());
        Assert.DoesNotContain("Client", await first.Content.ReadAsStringAsync());
        var known = await fixture.Admin.GetFromJsonAsync<JsonElement>(Search);
        Assert.Equal(book.GetProperty("id").GetGuid(), known[0].GetProperty("id").GetGuid());
        await fixture.WithDb(async db =>
        {
            var stored = await db.Books.SingleAsync();
            Assert.Equal(BookCatalogKind.Imported, stored.CatalogKind);
            Assert.Equal("Editora de teste", stored.Publisher);
            Assert.Equal("pt-BR", stored.Language);
            Assert.Equal("9780306406157", stored.Isbn13);
            Assert.Null(stored.Description);
            Assert.Null(stored.OwnerUserId);
            Assert.Equal(EditorialContentStatus.None, stored.ReadingOutlineStatus);
            Assert.Equal(EditorialContentStatus.None, stored.LoreStatus);
            Assert.Empty(await db.UserBooks.ToListAsync());
            Assert.Empty(await db.MemoryPackRequests.ToListAsync());
            Assert.Empty(await db.ReadingUnits.ToListAsync());
            Assert.Empty(await db.LoreEntities.ToListAsync());
        });
    }

    [Fact]
    public async Task Reader_and_admin_share_import_and_only_missing_metadata_is_enriched()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Metadata = fixture.Provider.Metadata with { Publisher = null, Subtitle = null };
        var first = await Write(fixture.Reader, "/api/library/external", Reference);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("book").GetProperty("id").GetGuid();
        Guid unitId = default;
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync();
            book.ReadingOutlineStatus = EditorialContentStatus.Published;
            book.LoreStatus = EditorialContentStatus.Published;
            var unit = new ReadingUnit { BookId = book.Id, Title = "Unidade existente",
                SafeLabel = "Capítulo 1", Slug = "capitulo-1", OrderIndex = 1 };
            unitId = unit.Id;
            db.ReadingUnits.Add(unit);
            var library = await db.UserBooks.SingleAsync();
            library.CurrentReadingUnitId = unit.Id;
            library.CurrentPage = 17;
            await db.SaveChangesAsync();
        });
        fixture.Provider.Metadata = fixture.Provider.Metadata with
        {
            Title = "Outro título do provedor", Author = "Outra autoria",
            Language = "en", Publisher = "Editora preenchida", Subtitle = "Subtítulo preenchido"
        };
        var imported = await Write(fixture.Admin, Import, Reference);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(id, (await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync();
            Assert.Equal("Livro externo", book.Title);
            Assert.Equal("Autora", book.Author);
            Assert.Equal("pt-BR", book.Language);
            Assert.Equal("Editora preenchida", book.Publisher);
            Assert.Equal("Subtítulo preenchido", book.Subtitle);
            Assert.Equal(EditorialContentStatus.Published, book.ReadingOutlineStatus);
            Assert.Equal(EditorialContentStatus.Published, book.LoreStatus);
            Assert.Equal(unitId, (await db.ReadingUnits.SingleAsync()).Id);
            var library = await db.UserBooks.SingleAsync();
            Assert.Equal(fixture.ReaderId, library.UserId);
            Assert.Equal(unitId, library.CurrentReadingUnitId);
            Assert.Equal(17, library.CurrentPage);
        });
    }

    [Fact]
    public async Task Concurrent_reader_and_admin_imports_reuse_one_shared_book()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Coordinate = true;
        var responses = await Task.WhenAll(Write(fixture.Admin, Import, Reference),
            Write(fixture.Reader, "/api/library/external", Reference));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync();
            var membership = await db.UserBooks.SingleAsync();
            Assert.Equal(book.Id, membership.BookId);
            Assert.Equal(fixture.ReaderId, membership.UserId);
        });
    }

    [Fact]
    public async Task Private_identifier_collision_is_never_imported_or_exposed_to_admin()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            db.Books.Add(new Book { Title = "Livro particular sigiloso", Author = "Autora",
                CatalogKind = BookCatalogKind.Private, OwnerUserId = fixture.ReaderId,
                ExternalProvider = "Fixture", ExternalId = "edition-1" });
            await db.SaveChangesAsync();
        });
        var response = await Write(fixture.Admin, Import, Reference);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("sigiloso", await response.Content.ReadAsStringAsync());
        await fixture.WithDb(async db => Assert.Empty(await db.UserBooks.ToListAsync()));
    }

    [Theory]
    [InlineData("disabled", 503)]
    [InlineData("failure", 503)]
    [InlineData("missing", 404)]
    [InlineData("invalid-metadata", 503)]
    [InlineData("invalid-reference", 400)]
    public async Task Import_failures_do_not_write_or_expose_provider_details(string mode, int status)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Enabled = mode != "disabled";
        fixture.Provider.Fail = mode == "failure";
        fixture.Provider.Missing = mode == "missing";
        if (mode == "invalid-metadata") fixture.Provider.Metadata = fixture.Provider.Metadata with { Title = " " };
        var response = await Write(fixture.Admin, Import,
            mode == "invalid-reference" ? new ExternalBookImportRequest("", "") : Reference);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.DoesNotContain("provider-secret", await response.Content.ReadAsStringAsync());
        await fixture.WithDb(async db => Assert.Empty(await db.Books.ToListAsync()));
    }

    private const string Search = "/api/admin/books/search?q=Livro";
    private const string Import = "/api/admin/books/external";
    private static readonly ExternalBookImportRequest Reference = new("Fixture", "edition-1");

    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Reader { get; init; }
        public required FakeProvider Provider { get; init; }
        public Guid AdminId { get; init; }
        public Guid ReaderId { get; init; }
        public static async Task<Fixture> CreateAsync()
        {
            var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-admin-import-{Guid.NewGuid():N}.db");
            var provider = new FakeProvider();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                    services.RemoveAll<MnemoraDbContext>();
                    services.AddDbContext<MnemoraDbContext>(options => options.UseSqlite($"Data Source={dbFile};Pooling=False"));
                    services.RemoveAll<IBookMetadataProvider>();
                    services.AddSingleton<IBookMetadataProvider>(provider);
                });
            });
            var admin = factory.CreateClient();
            var reader = factory.CreateClient();
            var adminId = Guid.NewGuid();
            var readerId = Guid.NewGuid();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                Assert.True((await roles.CreateAsync(new("Admin"))).Succeeded);
                foreach (var pair in new[] { (adminId, "admin"), (readerId, "reader") })
                {
                    var user = new IdentityUser<Guid> { Id = pair.Item1, UserName = $"{pair.Item2}@import.test", Email = $"{pair.Item2}@import.test" };
                    Assert.True((await users.CreateAsync(user, "StrongImportPass123")).Succeeded);
                    if (pair.Item1 == adminId) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                }
            }
            foreach (var pair in new[] { (admin, "admin"), (reader, "reader") })
                Assert.Equal(HttpStatusCode.OK, (await Write(pair.Item1, "/api/auth/login",
                    new { email = $"{pair.Item2}@import.test", password = "StrongImportPass123" })).StatusCode);
            return new() { Factory = factory, Admin = admin, Reader = reader, Provider = provider,
                AdminId = adminId, ReaderId = readerId };
        }
        public async Task WithDb(Func<MnemoraDbContext, Task> action)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<MnemoraDbContext>());
        }
        public void Dispose() { Admin.Dispose(); Reader.Dispose(); Factory.Dispose(); }
    }

    private sealed class FakeProvider : IBookMetadataProvider
    {
        public ExternalBookMetadata Metadata { get; set; } = new("Fixture", "edition-1", "Livro externo", "Autora",
            null, "0306406152", "9780306406157", "Editora de teste", new(2020, 1, 1), "pt-BR", WorkId: "work-1");
        public bool Enabled { get; set; } = true;
        public bool Fail { get; set; }
        public bool Missing { get; set; }
        public bool Coordinate { get; set; }
        public int SearchCalls { get; private set; }
        public string? LastQuery { get; private set; }
        private int gets;
        private readonly TaskCompletionSource paired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConfigured => Enabled;
        public Task<IReadOnlyList<ExternalBookMetadata>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            SearchCalls++;
            LastQuery = query;
            if (Fail) throw new HttpRequestException("provider-secret");
            return Task.FromResult<IReadOnlyList<ExternalBookMetadata>>([Metadata]);
        }
        public async Task<ExternalBookMetadata?> GetAsync(string provider, string externalId, CancellationToken cancellationToken)
        {
            if (Coordinate)
            {
                if (Interlocked.Increment(ref gets) == 2) paired.TrySetResult();
                await paired.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            if (Fail) throw new HttpRequestException("provider-secret");
            return !Missing && provider == Metadata.Provider && externalId == Metadata.ExternalId ? Metadata : null;
        }
    }
}
