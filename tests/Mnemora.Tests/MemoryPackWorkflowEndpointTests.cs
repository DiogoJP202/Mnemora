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
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackWorkflowEndpointTests
{
    [Fact]
    public async Task Reviewed_outline_import_is_safe_hierarchical_and_idempotent()
    {
        using var factory = CreateFactory();
        var adminEmail = $"outline-admin-{Guid.NewGuid():N}@test.local";
        Guid bookId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
            var user = new IdentityUser<Guid> { UserName = adminEmail, Email = adminEmail };
            Assert.True((await users.CreateAsync(user, "StrongAdminPass123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            var importedBook = new Book
            {
                Title = "Edição com sumário",
                Author = "Autora",
                CatalogKind = BookCatalogKind.Imported,
                ExternalProvider = "OpenLibrary",
                ExternalId = "OL28645793M",
                ReadingOutlineStatus = EditorialContentStatus.Draft,
                LoreStatus = EditorialContentStatus.Draft
            };
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(importedBook);
            await db.SaveChangesAsync();
            bookId = importedBook.Id;
        }

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await Write(client, HttpMethod.Post,
            "/api/auth/login", new
            {
                email = adminEmail,
                password = "StrongAdminPass123"
            })).StatusCode);
        var payload = new
        {
            sourceUrl = "https://openlibrary.org/books/OL28645793M",
            attribution = "Open Library",
            items = new object[]
            {
                new { title = "Parte Um", level = 1, page = 5 },
                new { title = "A Chegada", level = 2, page = 9 },
                new { title = "A Ponte", level = 2, page = 21 }
            }
        };
        var imported = await Write(client, HttpMethod.Post,
            $"/api/admin/books/{bookId}/reading-outline/import", payload);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var units = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/books/{bookId}/reading-units");
        Assert.Equal(3, units.GetArrayLength());
        Assert.Equal("Unidade 1", units[0].GetProperty("safeLabel").GetString());
        Assert.Equal(units[0].GetProperty("id").GetGuid(),
            units[1].GetProperty("parentUnitId").GetGuid());
        Assert.DoesNotContain("Parte Um",
            units[0].GetProperty("safeLabel").GetString());

        var repeated = await Write(client, HttpMethod.Post,
            $"/api/admin/books/{bookId}/reading-outline/import", payload);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var dbVerification = verification.ServiceProvider
            .GetRequiredService<MnemoraDbContext>();
        Assert.Equal(3, await dbVerification.ReadingUnits.CountAsync());
        var verifiedBook = await dbVerification.Books.SingleAsync(x => x.Id == bookId);
        Assert.Equal("Open Library", verifiedBook.ReadingOutlineAttribution);
        Assert.Equal("https://openlibrary.org/books/OL28645793M",
            verifiedBook.ReadingOutlineSourceUrl);
    }

    [Fact]
    public async Task Request_draft_and_publication_keep_each_layer_private_until_ready()
    {
        using var factory = CreateFactory();
        using var reader = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var readerEmail = $"reader-{Guid.NewGuid():N}@test.local";
        Assert.Equal(HttpStatusCode.OK, (await Write(reader, HttpMethod.Post,
            "/api/auth/register", new
            {
                email = readerEmail,
                password = "StrongTestPass123"
            })).StatusCode);

        Guid bookId;
        Guid privateEntityId;
        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var roles = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
            var adminUser = new IdentityUser<Guid>
            {
                UserName = adminEmail,
                Email = adminEmail
            };
            Assert.True((await users.CreateAsync(adminUser, "StrongAdminPass123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(adminUser, "Admin")).Succeeded);
            var readerUser = await users.FindByEmailAsync(readerEmail);
            Assert.NotNull(readerUser);

            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var book = new Book
            {
                Title = "Livro importado",
                Author = "Autora",
                CatalogKind = BookCatalogKind.Imported,
                ReadingOutlineStatus = EditorialContentStatus.None,
                LoreStatus = EditorialContentStatus.None
            };
            var privateBook = new Book
            {
                Title = "Livro privado",
                Author = "Leitor",
                CatalogKind = BookCatalogKind.Private,
                OwnerUserId = readerUser.Id,
                ReadingOutlineStatus = EditorialContentStatus.Draft,
                LoreStatus = EditorialContentStatus.Draft
            };
            db.Books.AddRange(book, privateBook);
            await db.SaveChangesAsync();
            var privateUnit = Unit(privateBook.Id, "Segredo privado");
            db.ReadingUnits.Add(privateUnit);
            await db.SaveChangesAsync();
            var privateEntity = new LoreEntity
            {
                BookId = privateBook.Id,
                FirstKnownAtUnitId = privateUnit.Id,
                Type = LoreEntityType.Character,
                Name = "Pessoa privada",
                Slug = "pessoa-privada"
            };
            db.LoreEntities.Add(privateEntity);
            db.UserBooks.Add(new UserBook
            {
                UserId = readerUser.Id,
                BookId = book.Id
            });
            await db.SaveChangesAsync();
            bookId = book.Id;
            privateEntityId = privateEntity.Id;
        }

        var firstRequest = await Write(reader, HttpMethod.Post,
            $"/api/books/{bookId}/memory-pack-request");
        var repeatedRequest = await Write(reader, HttpMethod.Post,
            $"/api/books/{bookId}/memory-pack-request");
        Assert.Equal(HttpStatusCode.OK, firstRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeatedRequest.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            Assert.Equal(1, await db.MemoryPackRequests.CountAsync());
        }
        var requestedBook = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}");
        Assert.Equal("Requested",
            requestedBook.GetProperty("memoryPackStatus").GetString());
        Assert.False(requestedBook.GetProperty("readingOutlineAvailable").GetBoolean());
        Assert.False(requestedBook.GetProperty("memoryPackAvailable").GetBoolean());

        using var admin = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await Write(admin, HttpMethod.Post,
            "/api/auth/login", new
            {
                email = adminEmail,
                password = "StrongAdminPass123"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/admin/entities/{privateEntityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(admin, HttpMethod.Put,
                $"/api/admin/entities/{privateEntityId}", new
                {
                    type = "Character",
                    name = "Tentativa",
                    slug = "tentativa",
                    firstKnownAtUnitId = Guid.NewGuid(),
                    importance = 1
                })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/prepare")).StatusCode);
        var unitResponse = await Write(admin, HttpMethod.Post,
            $"/api/admin/books/{bookId}/reading-units", new
            {
                title = "A chegada",
                safeLabel = "Capítulo 1",
                slug = "a-chegada",
                type = "Chapter",
                orderIndex = 10
            });
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        var unitId = (await unitResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        var entityResponse = await Write(admin, HttpMethod.Post,
            $"/api/admin/books/{bookId}/entities", new
            {
                type = "Character",
                name = "Nara",
                slug = "nara",
                shortDescription = "Uma viajante recém-chegada.",
                firstKnownAtUnitId = unitId,
                importance = 1
            });
        Assert.Equal(HttpStatusCode.Created, entityResponse.StatusCode);

        var draftBook = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}");
        Assert.Equal("Preparing", draftBook.GetProperty("memoryPackStatus").GetString());
        Assert.False(draftBook.GetProperty("readingOutlineAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/books/{bookId}/reading-units")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/books/{bookId}/entities")).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/publish-outline")).StatusCode);
        var outlinedBook = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}");
        Assert.True(outlinedBook.GetProperty("readingOutlineAvailable").GetBoolean());
        Assert.False(outlinedBook.GetProperty("memoryPackAvailable").GetBoolean());
        var units = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}/reading-units");
        Assert.Equal("Capítulo 1", units[0].GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/publish-lore")).StatusCode);
        var readyBook = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}");
        Assert.Equal("Available", readyBook.GetProperty("memoryPackStatus").GetString());
        Assert.True(readyBook.GetProperty("memoryPackAvailable").GetBoolean());
        Assert.Empty((await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}/entities")).EnumerateArray());

        Assert.Equal(HttpStatusCode.OK,
            (await Write(reader, HttpMethod.Patch,
                $"/api/library/{bookId}/progress", new
                {
                    currentReadingUnitId = unitId
                })).StatusCode);
        var known = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}/entities");
        Assert.Equal("Nara", Assert.Single(known.EnumerateArray())
            .GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/unpublish-lore")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/books/{bookId}/entities")).StatusCode);
        var withdrawn = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}");
        Assert.True(withdrawn.GetProperty("readingOutlineAvailable").GetBoolean());
        Assert.False(withdrawn.GetProperty("memoryPackAvailable").GetBoolean());

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/unpublish-outline")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/books/{bookId}/reading-units")).StatusCode);
        var pageUpdate = await Write(reader, HttpMethod.Patch,
            $"/api/library/{bookId}/progress", new
            {
                currentPage = 42,
                status = "Paused"
            });
        Assert.Equal(HttpStatusCode.OK, pageUpdate.StatusCode);
        var retainedProgress = await pageUpdate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(unitId, retainedProgress.GetProperty("currentReadingUnitId").GetGuid());
        Assert.Equal(42, retainedProgress.GetProperty("currentPage").GetInt32());
        Assert.Equal("Paused", retainedProgress.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(reader, HttpMethod.Patch,
                $"/api/library/{bookId}/progress", new
                {
                    currentReadingUnitId = unitId
                })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/publish-outline")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Write(admin, HttpMethod.Post,
                $"/api/admin/books/{bookId}/memory-pack/publish-lore")).StatusCode);
        var restoredKnowledge = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}/entities");
        Assert.Equal("Nara", Assert.Single(restoredKnowledge.EnumerateArray())
            .GetProperty("name").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var dbFile = Path.Combine(Path.GetTempPath(),
            $"mnemora-pack-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
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
    }

    private static ReadingUnit Unit(Guid bookId, string title) => new()
    {
        BookId = bookId,
        Title = title,
        SafeLabel = "Capítulo 1",
        Slug = "capitulo-1",
        Type = ReadingUnitType.Chapter,
        OrderIndex = 10
    };

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
}
