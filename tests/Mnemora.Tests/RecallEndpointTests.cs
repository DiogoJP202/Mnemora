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

public sealed class RecallEndpointTests
{
    [Fact]
    public async Task Recall_requires_authentication()
    {
        using var factory = CreateFactory("anonymous");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/books/00000000-0000-0000-0000-000000000001/recall?q=Nara");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Recall_finds_known_fields_at_the_exact_boundary_without_future_text()
    {
        using var factory = CreateFactory("known-fields");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var userId = await Register(client);
        var story = await SeedStory(factory, userId, currentOrder: 20);

        await AssertSingleResult(client, story.BookId, "Nara", "Nara");
        await AssertSingleResult(client, story.BookId, "cartografa da nevoa", "Nara");
        await AssertSingleResult(client, story.BookId, "Vigia azul", "Nara");
        await AssertSingleResult(client, story.BookId, "mapa ambar", "Nara");
        await AssertSingleResult(client, story.BookId, "sinal do farol", "Nara");

        // The entity itself is revealed exactly at the reader's current unit.
        await AssertSingleResult(client, story.BookId, "Tomas Limite", "Tomas Limite");

        await AssertEmpty(client, story.BookId, "Iria Futura");
        await AssertEmpty(client, story.BookId, "Rainha oculta");
        await AssertEmpty(client, story.BookId, "herdeira do trono");
        await AssertEmpty(client, story.BookId, "coroa secreta");

        var broadResponse = await client.GetAsync(
            $"/api/books/{story.BookId}/recall?q=registro");
        broadResponse.EnsureSuccessStatusCode();
        var json = await broadResponse.Content.ReadAsStringAsync();
        Assert.Contains("Nara", json);
        Assert.Contains("Tomas Limite", json);
        Assert.DoesNotContain("Iria Futura", json);
        Assert.DoesNotContain("Registro futuro proibido", json);
        Assert.DoesNotContain("Rainha oculta", json);
        Assert.DoesNotContain("herdeira do trono", json);
        Assert.DoesNotContain("coroa secreta", json);
    }

    [Fact]
    public async Task Rolling_progress_back_revokes_recall_results()
    {
        using var factory = CreateFactory("rollback");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var userId = await Register(client);
        var story = await SeedStory(factory, userId, currentOrder: 30);

        await AssertSingleResult(client, story.BookId, "Iria Futura", "Iria Futura");
        await AssertSingleResult(client, story.BookId, "Rainha oculta", "Nara");
        await AssertSingleResult(client, story.BookId, "herdeira do trono", "Nara");
        await AssertSingleResult(client, story.BookId, "coroa secreta", "Nara");

        var rollback = await Write(client, HttpMethod.Patch,
            $"/api/library/{story.BookId}/progress",
            new { currentReadingUnitId = story.Unit20Id });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        await AssertEmpty(client, story.BookId, "Iria Futura");
        await AssertEmpty(client, story.BookId, "Rainha oculta");
        await AssertEmpty(client, story.BookId, "herdeira do trono");
        await AssertEmpty(client, story.BookId, "coroa secreta");
    }

    [Fact]
    public async Task Recall_validates_the_query_without_revealing_all_known_entities()
    {
        using var factory = CreateFactory("validation");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var userId = await Register(client);
        var story = await SeedStory(factory, userId, currentOrder: 20);

        await AssertEmptyQuery(client, $"/api/books/{story.BookId}/recall");
        await AssertEmptyQuery(client, $"/api/books/{story.BookId}/recall?q=");
        await AssertEmptyQuery(client, $"/api/books/{story.BookId}/recall?q=%20%20%20");

        var maximumLength = Uri.EscapeDataString(new string('a', 100));
        (await client.GetAsync($"/api/books/{story.BookId}/recall?q={maximumLength}"))
            .EnsureSuccessStatusCode();
        var tooLong = Uri.EscapeDataString(new string('a', 101));
        var response = await client.GetAsync(
            $"/api/books/{story.BookId}/recall?q={tooLong}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Recall_returns_not_found_outside_the_current_users_library()
    {
        using var factory = CreateFactory("ownership");
        using var owner = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var anotherReader = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var ownerId = await Register(owner);
        await Register(anotherReader);
        var story = await SeedStory(factory, ownerId, currentOrder: 20);
        var outsideBookId = await SeedBookWithoutLibraryEntry(factory);

        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync(
                $"/api/books/{outsideBookId}/recall?q=Nara")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherReader.GetAsync(
                $"/api/books/{story.BookId}/recall?q=Nara")).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string name)
    {
        var dbFile = Path.Combine(Path.GetTempPath(),
            $"mnemora-recall-{name}-{Guid.NewGuid():N}.db");
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

    private static async Task<StoryIds> SeedStory(
        WebApplicationFactory<Program> factory, Guid userId, int currentOrder)
    {
        var book = new Book { Title = "Memorias de teste", Author = "Equipe Mnemora" };
        var unit10 = Unit(book.Id, 10);
        var unit20 = Unit(book.Id, 20);
        var unit30 = Unit(book.Id, 30);
        var nara = Entity(book.Id, unit10.Id, "Nara", "Registro seguro da cartografa da nevoa");
        var boundary = Entity(book.Id, unit20.Id, "Tomas Limite", "Registro seguro do limite");
        var future = Entity(book.Id, unit30.Id, "Iria Futura", "Registro futuro proibido");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        db.Books.Add(book);
        await db.SaveChangesAsync();
        db.ReadingUnits.AddRange(unit10, unit20, unit30);
        await db.SaveChangesAsync();
        db.LoreEntities.AddRange(nara, boundary, future);
        db.UserBooks.Add(new UserBook
        {
            UserId = userId,
            BookId = book.Id,
            CurrentReadingUnitId = currentOrder switch
            {
                10 => unit10.Id,
                20 => unit20.Id,
                30 => unit30.Id,
                _ => throw new ArgumentOutOfRangeException(nameof(currentOrder))
            },
            Status = UserBookStatus.Reading
        });
        await db.SaveChangesAsync();
        db.EntityAliases.AddRange(
            new EntityAlias
            {
                EntityId = nara.Id,
                Alias = "VÍGIA ÁZUL",
                RevealAtUnitId = unit20.Id
            },
            new EntityAlias
            {
                EntityId = nara.Id,
                Alias = "Rainha oculta",
                RevealAtUnitId = unit30.Id
            });
        db.LoreFacts.AddRange(
            new LoreFact
            {
                EntityId = nara.Id,
                Content = "Encontrou o mapa ambar.",
                MemoryHint = "Sinal do farol",
                RevealAtUnitId = unit20.Id,
                Importance = 2
            },
            new LoreFact
            {
                EntityId = nara.Id,
                Content = "E a herdeira do trono.",
                MemoryHint = "Coroa secreta",
                RevealAtUnitId = unit30.Id,
                Importance = 10
            });
        await db.SaveChangesAsync();

        return new StoryIds(book.Id, unit20.Id);
    }

    private static async Task<Guid> SeedBookWithoutLibraryEntry(
        WebApplicationFactory<Program> factory)
    {
        var book = new Book { Title = "Fora da estante", Author = "Equipe Mnemora" };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private static ReadingUnit Unit(Guid bookId, int order) => new()
    {
        BookId = bookId,
        Title = $"Capitulo {order}",
        SafeLabel = $"Unidade {order}",
        Slug = $"unit-{order}",
        OrderIndex = order
    };

    private static LoreEntity Entity(
        Guid bookId, Guid firstKnownAtUnitId, string name, string description) => new()
    {
        BookId = bookId,
        FirstKnownAtUnitId = firstKnownAtUnitId,
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-'),
        ShortDescription = description,
        Type = LoreEntityType.Character
    };

    private static async Task AssertSingleResult(
        HttpClient client, Guid bookId, string query, string expectedName)
    {
        var response = await client.GetAsync(
            $"/api/books/{bookId}/recall?q={Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Single(result.EnumerateArray());
        Assert.Equal(expectedName, result[0].GetProperty("name").GetString());
    }

    private static async Task AssertEmpty(HttpClient client, Guid bookId, string query)
    {
        var response = await client.GetAsync(
            $"/api/books/{bookId}/recall?q={Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(result.EnumerateArray());
    }

    private static async Task AssertEmptyQuery(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Empty(result.EnumerateArray());
    }

    private static async Task<Guid> Register(HttpClient client)
    {
        var response = await Write(client, HttpMethod.Post, "/api/auth/register", new
        {
            email = $"recall-{Guid.NewGuid():N}@test.local",
            password = "StrongTestPass123"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        return user.GetProperty("id").GetGuid();
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

    private sealed record StoryIds(Guid BookId, Guid Unit20Id);
}
