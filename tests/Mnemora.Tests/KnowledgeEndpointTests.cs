using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class KnowledgeEndpointTests
{
    [Fact]
    public async Task Http_endpoints_return_only_known_lore_and_no_future_metadata()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-{Guid.NewGuid():N}.db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={dbFile}"
                }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/books/00000000-0000-0000-0000-000000000001/entities")).StatusCode);
        var csrfResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrfResponse.GetProperty("token").GetString();
        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "reader@test.local", password = "StrongTestPass123" })
        };
        register.Headers.Add("X-CSRF-TOKEN", token);
        var registerResponse = await client.SendAsync(register);
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = registered.GetProperty("id").GetGuid();

        var book = new Book { Title = "O Arquivo da Neblina", Author = "Equipe Mnemora" };
        var unit10 = Unit(book.Id, 10);
        var unit20 = Unit(book.Id, 20);
        var unit40 = Unit(book.Id, 40);
        var known = Entity(book.Id, unit10.Id, "Nara");
        var future = Entity(book.Id, unit40.Id, "Iria");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(book);
            await db.SaveChangesAsync();
            db.ReadingUnits.AddRange(unit10, unit20, unit40);
            await db.SaveChangesAsync();
            db.LoreEntities.AddRange(known, future);
            db.UserBooks.Add(new UserBook
            {
                UserId = userId, BookId = book.Id,
                CurrentReadingUnitId = unit20.Id, Status = UserBookStatus.Reading
            });
            await db.SaveChangesAsync();
            db.EntityAliases.Add(new EntityAlias
            {
                EntityId = known.Id, Alias = "Rainha oculta", RevealAtUnitId = unit40.Id
            });
            db.LoreFacts.Add(new LoreFact
            {
                EntityId = known.Id, Content = "Segredo do farol.", RevealAtUnitId = unit40.Id
            });
            await db.SaveChangesAsync();
        }

        var entities = await client.GetAsync($"/api/books/{book.Id}/entities");
        Assert.Equal(HttpStatusCode.OK, entities.StatusCode);
        Assert.Equal("no-store", entities.Headers.CacheControl?.ToString());
        var list = await entities.Content.ReadAsStringAsync();
        Assert.Contains("Nara", list);
        Assert.DoesNotContain("Iria", list);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/entities/{future.Id}")).StatusCode);
        Assert.Equal("[]",
            await client.GetStringAsync($"/api/books/{book.Id}/recall?q=Rainha%20oculta"));
        Assert.Equal("[]",
            await client.GetStringAsync($"/api/books/{book.Id}/entities/search?q=Segredo%20do%20farol"));
    }

    private static ReadingUnit Unit(Guid bookId, int order) => new()
    {
        BookId = bookId, Title = $"Capítulo {order}", SafeLabel = $"Unidade {order}",
        Slug = $"unit-{order}", OrderIndex = order
    };

    private static LoreEntity Entity(Guid bookId, Guid first, string name) => new()
    {
        BookId = bookId, FirstKnownAtUnitId = first,
        Name = name, Slug = name.ToLowerInvariant(), Type = LoreEntityType.Character
    };
}
