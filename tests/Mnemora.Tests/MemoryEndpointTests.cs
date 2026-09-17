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

public sealed class MemoryEndpointTests
{
    [Fact]
    public async Task Notes_are_private_validated_and_follow_progress_rollbacks()
    {
        using var factory = CreateFactory("notes");
        using var owner = Client(factory);
        using var other = Client(factory);
        var ownerId = await Register(owner);
        var otherId = await Register(other);
        var story = await SeedStory(factory, ownerId, otherId);

        var general = await CreateNote(owner, story.BookId,
            new { content = "  Minha hipótese  " }, HttpStatusCode.Created);
        Assert.Equal("Minha hipótese", general.GetProperty("content").GetString());
        var entityNote = await CreateNote(owner, story.BookId,
            new { content = "Lembrar desta pessoa", entityId = story.Entity20Id },
            HttpStatusCode.Created);
        await CreateNote(owner, story.BookId,
            new { content = "Anotação do capítulo", readingUnitId = story.Unit20Id },
            HttpStatusCode.Created);

        await AssertRejectedNote(owner, story.BookId,
            new { content = "Futuro", entityId = story.FutureEntityId });
        await AssertRejectedNote(owner, story.BookId,
            new { content = "Futuro", readingUnitId = story.Unit30Id });
        await AssertRejectedNote(owner, story.BookId,
            new { content = "Outro livro", entityId = story.OtherEntityId });
        await AssertRejectedNote(owner, story.BookId,
            new { content = "Outro livro", readingUnitId = story.OtherUnitId });
        await AssertRejectedNote(owner, story.BookId, new { content = "   " });
        await AssertRejectedNote(owner, story.BookId,
            new { content = new string('x', 4001) });

        Assert.Equal(3, (await Notes(owner, story.BookId)).GetArrayLength());
        Assert.Empty((await Notes(other, story.BookId)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(other, HttpMethod.Put,
                $"/api/notes/{general.GetProperty("id").GetGuid()}",
                new { content = "Tentativa alheia" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(other, HttpMethod.Delete,
                $"/api/notes/{general.GetProperty("id").GetGuid()}")).StatusCode);

        var updated = await Write(owner, HttpMethod.Put,
            $"/api/notes/{general.GetProperty("id").GetGuid()}",
            new { content = "  Hipótese revista  " });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Hipótese revista",
            (await updated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("content").GetString());

        await SetProgress(owner, story.BookId, story.Unit10Id);
        var afterRollback = await Notes(owner, story.BookId);
        Assert.Single(afterRollback.EnumerateArray());
        Assert.Equal("Hipótese revista", afterRollback[0].GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync(
                $"/api/books/{story.BookId}/notes?entityId={story.Entity20Id}"))
                .StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(owner, HttpMethod.Put,
                $"/api/notes/{entityNote.GetProperty("id").GetGuid()}",
                new { content = "Não deve reaparecer" })).StatusCode);

        await SetProgress(owner, story.BookId, story.Unit20Id);
        Assert.Single((await Notes(owner, story.BookId, story.Entity20Id)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent,
            (await Write(owner, HttpMethod.Delete,
                $"/api/notes/{entityNote.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(2, (await Notes(owner, story.BookId)).GetArrayLength());
    }

    [Fact]
    public async Task Review_contains_at_most_five_known_entities_and_records_feedback()
    {
        using var factory = CreateFactory("review");
        using var reader = Client(factory);
        using var noProgressReader = Client(factory);
        var readerId = await Register(reader);
        var noProgressId = await Register(noProgressReader);
        var story = await SeedReviewStory(factory, readerId, noProgressId);

        var review = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{story.BookId}/review");
        Assert.Equal(5, review.GetArrayLength());
        Assert.DoesNotContain(review.EnumerateArray(), x =>
            x.GetProperty("id").GetGuid() == story.FutureEntityId);
        var noProgress = await noProgressReader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{story.BookId}/review");
        Assert.Empty(noProgress.EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(reader, HttpMethod.Post,
                $"/api/books/{story.BookId}/review/{story.FutureEntityId}",
                new { remembered = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(reader, HttpMethod.Post,
                $"/api/books/{story.BookId}/review/{story.KnownEntityIds[0]}",
                new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Write(reader, HttpMethod.Post,
                $"/api/books/{story.BookId}/review/{story.KnownEntityIds[0]}",
                new { remembered = false })).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var activity = await db.UserRecallActivities.SingleAsync();
            Assert.Equal(readerId, activity.UserId);
            Assert.Equal(story.BookId, activity.BookId);
            Assert.Equal(story.KnownEntityIds[0], activity.EntityId);
            Assert.False(activity.Remembered);
        }

        await SetProgress(reader, story.BookId, story.Unit10Id);
        review = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/books/{story.BookId}/review");
        Assert.Equal(2, review.GetArrayLength());
        Assert.All(review.EnumerateArray(), x =>
            Assert.Contains(x.GetProperty("id").GetGuid(), story.Unit10EntityIds));
    }

    [Fact]
    public async Task Memory_storage_limits_notes_and_prunes_review_history()
    {
        using var factory = CreateFactory("limits");
        using var reader = Client(factory);
        using var noProgressReader = Client(factory);
        var readerId = await Register(reader);
        var noProgressId = await Register(noProgressReader);
        var story = await SeedReviewStory(factory, readerId, noProgressId);
        Guid[] oldestActivityIds;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.UserNotes.AddRange(Enumerable.Range(1, 199).Select(index => new UserNote
            {
                UserId = readerId,
                BookId = story.BookId,
                Content = $"Nota {index}"
            }));

            var historyStart = DateTime.UtcNow.AddHours(-1);
            var activities = Enumerable.Range(0, 500).Select(index =>
                new UserRecallActivity
                {
                    UserId = readerId,
                    BookId = story.BookId,
                    EntityId = story.KnownEntityIds[0],
                    Remembered = index % 2 == 0,
                    At = historyStart.AddSeconds(index)
                }).ToArray();
            oldestActivityIds = activities[..2].Select(x => x.Id).ToArray();
            db.UserRecallActivities.AddRange(activities);
            await db.SaveChangesAsync();
        }

        var noteWrites = await Task.WhenAll(
            Write(reader, HttpMethod.Post, $"/api/books/{story.BookId}/notes",
                new { content = "Nota concorrente A" }),
            Write(reader, HttpMethod.Post, $"/api/books/{story.BookId}/notes",
                new { content = "Nota concorrente B" }));
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            noteWrites.Select(x => x.StatusCode).Order().ToArray());

        var reviewWrites = await Task.WhenAll(
            Write(reader, HttpMethod.Post,
                $"/api/books/{story.BookId}/review/{story.KnownEntityIds[0]}",
                new { remembered = true }),
            Write(reader, HttpMethod.Post,
                $"/api/books/{story.BookId}/review/{story.KnownEntityIds[0]}",
                new { remembered = false }));
        Assert.All(reviewWrites,
            response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            Assert.Equal(200, await db.UserNotes.CountAsync(x =>
                x.UserId == readerId && x.BookId == story.BookId));
            Assert.Equal(500, await db.UserRecallActivities.CountAsync(x =>
                x.UserId == readerId && x.BookId == story.BookId));
            Assert.False(await db.UserRecallActivities.AnyAsync(x =>
                oldestActivityIds.Contains(x.Id)));
        }
    }

    [Fact]
    public async Task Memory_endpoints_require_authentication_and_library_ownership()
    {
        using var factory = CreateFactory("access");
        using var anonymous = factory.CreateClient();
        var bookId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/books/{bookId}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/books/{bookId}/review")).StatusCode);

        using var user = Client(factory);
        await Register(user);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.Books.Add(new Book { Id = bookId, Title = "Fora", Author = "Teste" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await user.GetAsync($"/api/books/{bookId}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await user.GetAsync($"/api/books/{bookId}/review")).StatusCode);
    }

    private static async Task<NoteStory> SeedStory(
        WebApplicationFactory<Program> factory, Guid ownerId, Guid otherId)
    {
        var book = new Book { Title = "Notas", Author = "Teste" };
        var u10 = Unit(book.Id, 10);
        var u20 = Unit(book.Id, 20);
        var u30 = Unit(book.Id, 30);
        var entity20 = Entity(book.Id, u20.Id, "Pessoa conhecida", 2);
        var future = Entity(book.Id, u30.Id, "Pessoa futura", 1);
        var otherBook = new Book { Title = "Outro", Author = "Teste" };
        var otherUnit = Unit(otherBook.Id, 10);
        var otherEntity = Entity(otherBook.Id, otherUnit.Id, "De outro livro", 1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        db.Books.AddRange(book, otherBook);
        await db.SaveChangesAsync();
        db.ReadingUnits.AddRange(u10, u20, u30, otherUnit);
        await db.SaveChangesAsync();
        db.LoreEntities.AddRange(entity20, future, otherEntity);
        db.UserBooks.AddRange(
            new UserBook { UserId = ownerId, BookId = book.Id,
                CurrentReadingUnitId = u20.Id, Status = UserBookStatus.Reading },
            new UserBook { UserId = otherId, BookId = book.Id,
                CurrentReadingUnitId = u20.Id, Status = UserBookStatus.Reading });
        await db.SaveChangesAsync();
        return new NoteStory(book.Id, u10.Id, u20.Id, u30.Id,
            entity20.Id, future.Id, otherUnit.Id, otherEntity.Id);
    }

    private static async Task<ReviewStory> SeedReviewStory(
        WebApplicationFactory<Program> factory, Guid readerId, Guid noProgressId)
    {
        var book = new Book { Title = "Revisão", Author = "Teste" };
        var u10 = Unit(book.Id, 10);
        var u20 = Unit(book.Id, 20);
        var u30 = Unit(book.Id, 30);
        var entities = new[]
        {
            Entity(book.Id, u10.Id, "A", 9), Entity(book.Id, u10.Id, "B", 8),
            Entity(book.Id, u20.Id, "C", 7), Entity(book.Id, u20.Id, "D", 6),
            Entity(book.Id, u20.Id, "E", 5), Entity(book.Id, u20.Id, "F", 4),
            Entity(book.Id, u30.Id, "Futuro", 20)
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        db.Books.Add(book);
        await db.SaveChangesAsync();
        db.ReadingUnits.AddRange(u10, u20, u30);
        await db.SaveChangesAsync();
        db.LoreEntities.AddRange(entities);
        db.UserBooks.AddRange(
            new UserBook { UserId = readerId, BookId = book.Id,
                CurrentReadingUnitId = u20.Id, Status = UserBookStatus.Reading },
            new UserBook { UserId = noProgressId, BookId = book.Id });
        await db.SaveChangesAsync();
        return new ReviewStory(book.Id, u10.Id, entities[6].Id,
            entities[..6].Select(x => x.Id).ToArray(),
            entities[..2].Select(x => x.Id).ToArray());
    }

    private static async Task<JsonElement> CreateNote(
        HttpClient client, Guid bookId, object body, HttpStatusCode expected)
    {
        var response = await Write(client, HttpMethod.Post,
            $"/api/books/{bookId}/notes", body);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertRejectedNote(HttpClient client, Guid bookId, object body) =>
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(client, HttpMethod.Post, $"/api/books/{bookId}/notes", body)).StatusCode);

    private static async Task<JsonElement> Notes(
        HttpClient client, Guid bookId, Guid? entityId = null) =>
        await client.GetFromJsonAsync<JsonElement>(
            $"/api/books/{bookId}/notes{(entityId is null ? "" : $"?entityId={entityId}")}");

    private static async Task SetProgress(HttpClient client, Guid bookId, Guid unitId)
    {
        var response = await Write(client, HttpMethod.Patch,
            $"/api/library/{bookId}/progress", new { currentReadingUnitId = unitId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string name)
    {
        var dbFile = Path.Combine(Path.GetTempPath(),
            $"mnemora-memory-{name}-{Guid.NewGuid():N}.db");
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

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<Guid> Register(HttpClient client)
    {
        var response = await Write(client, HttpMethod.Post, "/api/auth/register", new
        {
            email = $"memory-{Guid.NewGuid():N}@test.local",
            password = "StrongTestPass123"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

    private static ReadingUnit Unit(Guid bookId, int order) => new()
    {
        BookId = bookId, OrderIndex = order, Title = $"Capítulo {order}",
        SafeLabel = $"Unidade {order}", Slug = $"unit-{order}"
    };

    private static LoreEntity Entity(
        Guid bookId, Guid unitId, string name, int importance) => new()
    {
        BookId = bookId, FirstKnownAtUnitId = unitId, Name = name,
        Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}",
        Type = LoreEntityType.Character, Importance = importance
    };

    private sealed record NoteStory(
        Guid BookId, Guid Unit10Id, Guid Unit20Id, Guid Unit30Id,
        Guid Entity20Id, Guid FutureEntityId, Guid OtherUnitId, Guid OtherEntityId);
    private sealed record ReviewStory(
        Guid BookId, Guid Unit10Id, Guid FutureEntityId,
        Guid[] KnownEntityIds, Guid[] Unit10EntityIds);
}
