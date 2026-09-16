using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class LoreExplorationEndpointTests
{
    [Fact]
    public async Task Exploration_endpoints_reveal_only_known_lore_and_revoke_it_after_rollback()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"mnemora-exploration-{Guid.NewGuid():N}.db");
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
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/books/00000000-0000-0000-0000-000000000001/factions")).StatusCode);
        await Register(client);

        var book = new Book { Title = "O Arquivo da Neblina", Author = "Equipe Mnemora" };
        var unit10 = Unit(book.Id, 10);
        var unit20 = Unit(book.Id, 20);
        var unit40 = Unit(book.Id, 40);
        var nara = Entity(book.Id, unit10.Id, "Nara", LoreEntityType.Character);
        var knownFaction = Entity(book.Id, unit10.Id, "Guilda do Porto", LoreEntityType.Faction);
        var knownLocation = Entity(book.Id, unit20.Id, "Ponte Velha", LoreEntityType.Location);
        var knownEvent = Entity(book.Id, unit20.Id, "Encontro na Ponte", LoreEntityType.Event);
        knownEvent.ChronologyIndex = 20;
        var futureCharacter = Entity(book.Id, unit40.Id, "Iria", LoreEntityType.Character);
        var futureFaction = Entity(book.Id, unit40.Id, "Ordem da Coroa", LoreEntityType.Faction);
        var futureLocation = Entity(book.Id, unit40.Id, "Farol Secreto", LoreEntityType.Location);
        var futureEvent = Entity(book.Id, unit40.Id, "Queda do Farol", LoreEntityType.Event);
        futureEvent.ChronologyIndex = 10;
        var userId = await UserId(client);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(book);
            await db.SaveChangesAsync();
            db.ReadingUnits.AddRange(unit10, unit20, unit40);
            await db.SaveChangesAsync();
            db.LoreEntities.AddRange(nara, knownFaction, knownLocation, knownEvent,
                futureCharacter, futureFaction, futureLocation, futureEvent);
            db.UserBooks.Add(new UserBook
            {
                UserId = userId, BookId = book.Id,
                CurrentReadingUnitId = unit20.Id, Status = UserBookStatus.Reading
            });
            await db.SaveChangesAsync();
            db.LoreFacts.AddRange(
                new LoreFact { EntityId = nara.Id, Content = "Nara conhece a guilda.", RevealAtUnitId = unit10.Id },
                new LoreFact { EntityId = nara.Id, Content = "Nara cruza a ponte.", RevealAtUnitId = unit20.Id },
                new LoreFact { EntityId = nara.Id, Content = "Segredo do farol.", RevealAtUnitId = unit40.Id });
            db.EntityAliases.Add(new EntityAlias
            {
                EntityId = nara.Id, Alias = "Rainha Oculta", RevealAtUnitId = unit40.Id
            });
            db.LoreRelations.AddRange(
                new LoreRelation
                {
                    BookId = book.Id, SourceEntityId = nara.Id, TargetEntityId = knownLocation.Id,
                    RelationType = "Visited", Label = "Travessia da ponte", RevealAtUnitId = unit20.Id
                },
                new LoreRelation
                {
                    BookId = book.Id, SourceEntityId = nara.Id, TargetEntityId = knownFaction.Id,
                    RelationType = "BelongsTo", Label = "Membro secreto", RevealAtUnitId = unit40.Id
                },
                new LoreRelation
                {
                    BookId = book.Id, SourceEntityId = nara.Id, TargetEntityId = futureCharacter.Id,
                    RelationType = "Knows", Label = "Encontro com Iria", RevealAtUnitId = unit40.Id
                });
            await db.SaveChangesAsync();
        }

        var prefix = $"/api/books/{book.Id}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"{prefix}/entities?type=Dragon")).StatusCode);
        await AssertNames(client, $"{prefix}/factions", "Guilda do Porto");
        await AssertNames(client, $"{prefix}/locations", "Ponte Velha");
        await AssertTimelineNames(client, $"{prefix}/timeline", "Encontro na Ponte");
        var detail = await ReadDetail(client, nara.Id);
        Assert.Equal(2, detail.GetProperty("facts").GetArrayLength());
        Assert.Single(detail.GetProperty("relations").EnumerateArray());
        Assert.Empty(detail.GetProperty("aliases").EnumerateArray());
        var detailJson = detail.ToString();
        Assert.DoesNotContain("Segredo do farol", detailJson);
        Assert.DoesNotContain("Rainha Oculta", detailJson);
        Assert.DoesNotContain("Membro secreto", detailJson);
        Assert.DoesNotContain("Encontro com Iria", detailJson);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/entities/{futureCharacter.Id}")).StatusCode);

        await SetProgress(client, book.Id, unit40.Id);
        await AssertNames(client, $"{prefix}/factions", "Guilda do Porto", "Ordem da Coroa");
        await AssertNames(client, $"{prefix}/locations", "Ponte Velha", "Farol Secreto");
        await AssertTimelineNames(client, $"{prefix}/timeline", "Queda do Farol", "Encontro na Ponte");
        detail = await ReadDetail(client, nara.Id);
        Assert.Equal(3, detail.GetProperty("facts").GetArrayLength());
        Assert.Equal(3, detail.GetProperty("relations").GetArrayLength());
        Assert.Single(detail.GetProperty("aliases").EnumerateArray());
        Assert.Contains("Segredo do farol", detail.ToString());
        Assert.Contains("Encontro com Iria", detail.ToString());
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/entities/{futureCharacter.Id}")).StatusCode);

        await SetProgress(client, book.Id, unit10.Id);
        await AssertNames(client, $"{prefix}/factions", "Guilda do Porto");
        await AssertNames(client, $"{prefix}/locations");
        await AssertTimelineNames(client, $"{prefix}/timeline");
        detail = await ReadDetail(client, nara.Id);
        Assert.Single(detail.GetProperty("facts").EnumerateArray());
        Assert.Empty(detail.GetProperty("relations").EnumerateArray());
        Assert.Empty(detail.GetProperty("aliases").EnumerateArray());
        Assert.DoesNotContain("Travessia da ponte", detail.ToString());
        Assert.DoesNotContain("Segredo do farol", detail.ToString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/entities/{knownLocation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/entities/{futureCharacter.Id}")).StatusCode);
    }

    private static async Task Register(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"reader-{Guid.NewGuid():N}@test.local", password = "StrongTestPass123"
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static async Task<Guid> UserId(HttpClient client)
    {
        var user = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return user.GetProperty("id").GetGuid();
    }

    private static async Task SetProgress(HttpClient client, Guid bookId, Guid unitId)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"/api/library/{bookId}/progress")
        {
            Content = JsonContent.Create(new { currentReadingUnitId = unitId })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static async Task AssertNames(HttpClient client, string path, params string[] expected)
    {
        var rows = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(
            expected.Order(StringComparer.Ordinal).ToArray(),
            rows.EnumerateArray()
                .Select(row => row.GetProperty("name").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task AssertTimelineNames(HttpClient client, string path, params string[] expected)
    {
        var rows = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(expected, rows.EnumerateArray()
            .Select(row => row.GetProperty("entity").GetProperty("name").GetString()).ToArray());
    }

    private static async Task<JsonElement> ReadDetail(HttpClient client, Guid entityId)
    {
        var response = await client.GetAsync($"/api/entities/{entityId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static ReadingUnit Unit(Guid bookId, int order) => new()
    {
        BookId = bookId, Title = $"Capítulo {order}", SafeLabel = $"Unidade {order}",
        Slug = $"unit-{order}", OrderIndex = order
    };

    private static LoreEntity Entity(Guid bookId, Guid first, string name, LoreEntityType type) => new()
    {
        BookId = bookId, FirstKnownAtUnitId = first,
        Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-'), Type = type
    };
}
