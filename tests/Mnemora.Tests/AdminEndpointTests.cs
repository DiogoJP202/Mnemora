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
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class AdminEndpointTests
{
    [Fact]
    public async Task Admin_crud_validates_reveal_points_and_preview_uses_safe_projection()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-admin-{Guid.NewGuid():N}.db");
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
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/books")).StatusCode);
        var normalEmail = $"normal-{Guid.NewGuid():N}@test.local";
        Assert.Equal(HttpStatusCode.OK, (await Write(anonymous, "/api/auth/register",
            new { email = normalEmail, password = "StrongTestPass123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await anonymous.GetAsync("/api/admin/books")).StatusCode);

        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            await roles.CreateAsync(new IdentityRole<Guid>("Admin"));
            var admin = new IdentityUser<Guid> { UserName = adminEmail, Email = adminEmail };
            Assert.True((await users.CreateAsync(admin, "StrongAdminPass123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "Admin")).Succeeded);
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await Write(client, "/api/auth/login",
            new { email = adminEmail, password = "StrongAdminPass123" })).StatusCode);

        var bookResponse = await Write(client, "/api/admin/books",
            new { title = "Livro de teste", author = "Equipe Mnemora" });
        Assert.Equal(HttpStatusCode.Created, bookResponse.StatusCode);
        var bookId = (await bookResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var unit10 = await CreateUnit(client, bookId, 10);
        var unit20 = await CreateUnit(client, bookId, 20);
        var invalidEntity = await Write(client, $"/api/admin/books/{bookId}/entities", new
        {
            type = "Character", name = "Inválida", slug = "invalida",
            firstKnownAtUnitId = Guid.NewGuid(), importance = 1
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEntity.StatusCode);
        var knownId = await CreateEntity(client, bookId, unit10, "Nara");
        var futureId = await CreateEntity(client, bookId, unit20, "Iria");

        var earlyAlias = await Write(client, $"/api/admin/entities/{futureId}/aliases", new
        {
            alias = "Antes da entrada", revealAtUnitId = unit10
        });
        Assert.Equal(HttpStatusCode.BadRequest, earlyAlias.StatusCode);

        var earlyFact = await Write(client, $"/api/admin/entities/{futureId}/facts", new
        {
            content = "Fato cedo demais.", type = "Description", importance = 1,
            revealAtUnitId = unit10
        });
        Assert.Equal(HttpStatusCode.BadRequest, earlyFact.StatusCode);
        var validFact = await Write(client, $"/api/admin/entities/{knownId}/facts", new
        {
            content = "Nara encontra Iria.", type = "Description", importance = 1,
            revealAtUnitId = unit20
        });
        Assert.Equal(HttpStatusCode.Created, validFact.StatusCode);
        var earlyRelation = await Write(client, $"/api/admin/books/{bookId}/relations", new
        {
            sourceEntityId = knownId, targetEntityId = futureId,
            relationType = "Meets", revealAtUnitId = unit10
        });
        Assert.Equal(HttpStatusCode.BadRequest, earlyRelation.StatusCode);
        var validRelation = await Write(client, $"/api/admin/books/{bookId}/relations", new
        {
            sourceEntityId = knownId, targetEntityId = futureId,
            relationType = "Meets", revealAtUnitId = unit20
        });
        Assert.Equal(HttpStatusCode.Created, validRelation.StatusCode);
        var unit30 = await CreateUnit(client, bookId, 30);
        var unsafeFirstKnownChange = await Write(client, $"/api/admin/entities/{knownId}", new
        {
            type = "Character", name = "Nara", slug = "nara",
            firstKnownAtUnitId = unit30, importance = 1
        }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Conflict, unsafeFirstKnownChange.StatusCode);
        var unsafeMove = await Write(client,
            $"/api/admin/books/{bookId}/reading-units/{unit20}/move?direction=up", new { });
        Assert.Equal(HttpStatusCode.Conflict, unsafeMove.StatusCode);

        var preview10 = await client.GetStringAsync(
            $"/api/admin/books/{bookId}/preview?atUnitId={unit10}");
        Assert.Contains("Nara", preview10);
        Assert.DoesNotContain("Iria", preview10);
        var detail10 = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/entities/{knownId}/preview?atUnitId={unit10}");
        Assert.Empty(detail10.GetProperty("facts").EnumerateArray());
        Assert.Empty(detail10.GetProperty("relations").EnumerateArray());
        var preview20 = await client.GetStringAsync(
            $"/api/admin/books/{bookId}/preview?atUnitId={unit20}");
        Assert.Contains("Iria", preview20);
        var detail20 = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/entities/{knownId}/preview?atUnitId={unit20}");
        Assert.Single(detail20.GetProperty("facts").EnumerateArray());
        Assert.Single(detail20.GetProperty("relations").EnumerateArray());
    }

    private static async Task<Guid> CreateUnit(HttpClient client, Guid bookId, int order)
    {
        var response = await Write(client, $"/api/admin/books/{bookId}/reading-units", new
        {
            title = $"Capítulo {order}", safeLabel = $"Capítulo {order}",
            slug = $"chapter-{order}", type = "Chapter", orderIndex = order
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateEntity(HttpClient client, Guid bookId, Guid unitId, string name)
    {
        var response = await Write(client, $"/api/admin/books/{bookId}/entities", new
        {
            type = "Character", name, slug = name.ToLowerInvariant(),
            firstKnownAtUnitId = unitId, importance = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> Write(
        HttpClient client, string path, object body, HttpMethod? method = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}
