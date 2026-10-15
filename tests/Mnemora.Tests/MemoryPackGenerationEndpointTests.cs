using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackGenerationEndpointTests
{
    private static readonly GenerationSource[] Sources =
    [
        new("O armário", "A mascarada carrega um mapa dobrado.\n\nLia trabalha no cais e suspeita que A mascarada levou o sino, mas não tem prova."),
        new("O mapa", "A mascarada carrega um mapa dobrado."),
        new("O acordo", "Lia e A mascarada firmam um acordo de colaboração."),
        new("A assinatura", "A mascarada diz: Meu nome é Íria.\n\nO diário relata a Queda da Ponte de Sal, ocorrida sete anos antes.")
    ];

    [Fact]
    public async Task Admin_authentication_csrf_and_owner_boundaries_protect_all_generation_routes()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Factory.CreateClient();
        foreach (var route in new[] { fixture.Path("availability"), fixture.Path(Guid.NewGuid().ToString()) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Reader.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(fixture.Path(), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Write(fixture.Reader, fixture.Path(), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await fixture.Admin.PostAsJsonAsync(fixture.Path(), Request())).StatusCode);

        var status = await fixture.StartAsync();
        var runId = RunId(status);
        foreach (var suffix in new[] { runId.ToString(), $"{runId}/result" })
        {
            var denied = await fixture.OtherAdmin.GetAsync(fixture.Path(suffix));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
            Assert.DoesNotContain("mapa dobrado", await denied.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden,
                (await fixture.Reader.GetAsync(fixture.Path(suffix))).StatusCode);
        }
        foreach (var suffix in new[] { $"{runId}/next", $"{runId}/cancel" })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await Write(fixture.OtherAdmin, fixture.Path(suffix), NextBody(status))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await fixture.Admin.PostAsJsonAsync(fixture.Path(suffix), NextBody(status))).StatusCode);
        }
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Disabled_generation_reports_availability_and_never_uses_the_provider()
    {
        using var fixture = await Fixture.CreateAsync(enabled: false);
        var availabilityResponse = await fixture.Admin.GetAsync(fixture.Path("availability"));
        Assert.Equal(HttpStatusCode.OK, availabilityResponse.StatusCode);
        Assert.Equal("no-store", availabilityResponse.Headers.CacheControl?.ToString());
        var availability = await Read(availabilityResponse);
        Assert.False(availability["enabled"]!.GetValue<bool>());
        Assert.DoesNotContain("fake-test-key-no-network", availability.ToJsonString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await Write(fixture.Admin, fixture.Path(), Request())).StatusCode);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Sources_parse_and_start_are_read_only_and_require_edition_confirmation()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var parsedResponse = await Write(fixture.Admin, fixture.Path("parse"), new
        {
            text = "## Capítulo 1 — O armário\nUma viajante chega.\n\n## Capítulo 2 — O mapa\n"
        });
        Assert.Equal(HttpStatusCode.OK, parsedResponse.StatusCode);
        Assert.Equal("no-store", parsedResponse.Headers.CacheControl?.ToString());
        var parsed = await Read(parsedResponse);
        Assert.True(parsed["isValid"]!.GetValue<bool>());
        Assert.Equal(2, parsed["sources"]!.AsArray().Count);
        Assert.Contains(parsed["issues"]!.AsArray(), issue =>
            issue!["code"]!.GetValue<string>() == "missing_summary");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(fixture.Admin, fixture.Path(), Request() with { EditionConfirmed = false })).StatusCode);

        var status = await fixture.StartAsync();
        Assert.Equal("Ready", status["state"]!.GetValue<string>());
        Assert.Equal(0, status["completedUnits"]!.GetValue<int>());
        Assert.Equal(4, status["totalUnits"]!.GetValue<int>());
        var current = await fixture.Admin.GetAsync(fixture.Path(RunId(status).ToString()));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("no-store", current.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(Sources[0].Summary, (await Read(current)).ToJsonString());
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Private_books_existing_outline_and_changed_edition_block_generation()
    {
        using var fixture = await Fixture.CreateAsync();
        Guid privateId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var privateBook = new Book
            {
                Title = "Fonte particular", Author = "Leitor", CatalogKind = BookCatalogKind.Private,
                OwnerUserId = fixture.ReaderId, ReadingOutlineStatus = EditorialContentStatus.None,
                LoreStatus = EditorialContentStatus.None
            };
            db.Books.Add(privateBook);
            await db.SaveChangesAsync();
            privateId = privateBook.Id;
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(fixture.Admin, fixture.Path(bookId: privateId), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Admin.GetAsync(fixture.Path("availability", privateId))).StatusCode);

        var status = await fixture.StartAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Language = "en";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await Write(fixture.Admin, fixture.Path($"{RunId(status)}/next"), NextBody(status))).StatusCode);
        Assert.Equal(0, fixture.Provider.Calls);

        using var withOutline = await Fixture.CreateAsync();
        await using (var scope = withOutline.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            db.ReadingUnits.Add(new ReadingUnit
            {
                BookId = withOutline.BookId, Title = "Capítulo 1", SafeLabel = "Capítulo 1",
                Slug = "one", Type = ReadingUnitType.Chapter, OrderIndex = 1
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await Write(withOutline.Admin, withOutline.Path(), Request())).StatusCode);
        Assert.Equal(0, withOutline.Provider.Calls);
    }

    [Fact]
    public async Task Replaying_an_operation_after_a_lost_response_does_not_repeat_provider_work()
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.StartAsync();
        var runId = RunId(status);
        var body = NextBody(status);
        var firstResponse = await Write(fixture.Admin, fixture.Path($"{runId}/next"), body);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await Read(firstResponse);
        var replayResponse = await Write(fixture.Admin, fixture.Path($"{runId}/next"), body);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.True(JsonNode.DeepEquals(first, await Read(replayResponse)));
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(1, first["completedUnits"]!.GetValue<int>());

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var staleResponse = await Write(fixture.Admin, fixture.Path($"{runId}/next"), NextBody(status));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(1, (await fixture.StatusAsync(runId))["completedUnits"]!.GetValue<int>());
    }

    [Fact]
    public async Task Predicted_revision_cannot_bind_a_stale_source_after_another_step_finishes()
    {
        var pause = new PauseBooksRead();
        using var fixture = await Fixture.CreateAsync(interceptor: pause);
        var status = await fixture.StartAsync();
        var runId = RunId(status);
        fixture.Provider.BlockNext = true;
        var firstRequest = Write(fixture.Admin, fixture.Path($"{runId}/next"), NextBody(status));
        try
        {
            await fixture.Provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pause.Arm();
            // This revision does not exist yet. An old implementation captured source 1
            // before the query, then accepted revision 1 after the first call completed.
            var secondRequest = Write(fixture.Admin, fixture.Path($"{runId}/next"), new
            {
                operationId = Guid.NewGuid(), expectedRevision = status["revision"]!.GetValue<int>() + 1
            });
            var winner = await Task.WhenAny(secondRequest, pause.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
            HttpResponseMessage secondResponse;
            HttpResponseMessage firstResponse;
            if (winner == secondRequest)
            {
                secondResponse = await secondRequest;
                pause.Disarm();
                fixture.Provider.Release.TrySetResult(true);
                firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                // Regression path: the stale snapshot is held at its book query while
                // the first operation advances the session to the predicted revision.
                fixture.Provider.Release.TrySetResult(true);
                firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(10));
                fixture.Clock.Advance(TimeSpan.FromSeconds(61));
                pause.Release.TrySetResult(true);
                secondResponse = await secondRequest.WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.Equal(1, fixture.Provider.Calls);
            var current = await fixture.StatusAsync(runId);
            Assert.Equal(1, current["revision"]!.GetValue<int>());
            Assert.Equal(1, current["completedUnits"]!.GetValue<int>());
            Assert.Equal("Generated", current["coverage"]![0]!["status"]!.GetValue<string>());
            Assert.Equal("Pending", current["coverage"]![1]!["status"]!.GetValue<string>());
        }
        finally
        {
            pause.Disarm();
            pause.Release.TrySetResult(true);
            fixture.Provider.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Missing_sources_produce_explicit_partial_coverage_and_a_separate_valid_report()
    {
        using var fixture = await Fixture.CreateAsync();
        var sources = Sources.Select((source, index) => index == 1 ? source with { Summary = "" } : source).ToArray();
        var status = await fixture.StartAsync(Request(sources));
        var result = await fixture.CompleteAsync(status);
        Assert.True(result["validation"]!["isValid"]!.GetValue<bool>());
        Assert.Equal("Completed", result["status"]!["state"]!.GetValue<string>());
        var coverage = result["status"]!["coverage"]!.AsArray();
        Assert.Equal(4, coverage.Count);
        Assert.Contains(coverage, item => item!["status"]!.GetValue<string>() == "MissingSource");
        Assert.Equal(3, fixture.Provider.Calls);
        Assert.NotNull(result["report"]);
        Assert.True(result["report"]!["partial"]!.GetValue<bool>());
        Assert.NotEmpty(result["report"]!["evidence"]!.AsArray());
        Assert.DoesNotContain("evidence", result["document"]!.ToJsonString());
        Assert.NotEmpty(result["document"]!["entities"]!.AsArray());
        var validation = await Read(await Write(fixture.Admin, fixture.TransferPath("validate"), result["document"]));
        Assert.True(validation["isValid"]!.GetValue<bool>());
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Generated_package_uses_existing_atomic_draft_import_and_spoiler_boundaries()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var result = await fixture.CompleteAsync(await fixture.StartAsync());
        var document = result["document"]!;
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.True(result["validation"]!["isValid"]!.GetValue<bool>());
        Assert.Equal("pt-BR", document["book"]!["language"]!.GetValue<string>());
        Assert.NotNull(result["report"]);
        Assert.False(result["report"]!["partial"]!.GetValue<bool>());
        var canonical = result["documentJson"]!.GetValue<string>();
        Assert.True(JsonNode.DeepEquals(document, JsonNode.Parse(canonical)));
        Assert.Equal(result["report"]!["packHash"]!.GetValue<string>(),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        Assert.DoesNotContain("report", document.ToJsonString());
        Assert.Equal(4, fixture.Provider.Calls);

        var imported = await Write(fixture.Admin, fixture.TransferPath("import"), document);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var importedState = await fixture.SnapshotAsync();
        var repeated = await Write(fixture.Admin, fixture.TransferPath("import"), document);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True((await Read(repeated))["alreadyImported"]!.GetValue<bool>());
        Assert.Equal(importedState, await fixture.SnapshotAsync());
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            Assert.Equal(EditorialContentStatus.Draft, book.ReadingOutlineStatus);
            Assert.Equal(EditorialContentStatus.Draft, book.LoreStatus);
            Assert.Null(book.ReadingOutlinePublishedAt);
            Assert.Null(book.LorePublishedAt);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Reader.GetAsync($"/api/books/{fixture.BookId}/entities")).StatusCode);
        await fixture.PublishAsync();
        var units = await fixture.UnitIdsAsync();
        Guid maskedId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            maskedId = await scope.ServiceProvider.GetRequiredService<MnemoraDbContext>().LoreEntities
                .Where(entity => entity.BookId == fixture.BookId && entity.Name == "A mascarada")
                .Select(entity => entity.Id).SingleAsync();
        await Progress("capitulo-001");
        var early = await fixture.Reader.GetFromJsonAsync<JsonObject>($"/api/entities/{maskedId}");
        Assert.Empty(early!["aliases"]!.AsArray());
        Assert.Empty(early["relations"]!.AsArray());
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/recall?q={Uri.EscapeDataString("Íria")}"));

        await Progress("capitulo-003");
        var third = await fixture.Reader.GetFromJsonAsync<JsonObject>($"/api/entities/{maskedId}");
        Assert.Empty(third!["aliases"]!.AsArray());
        Assert.Single(third["relations"]!.AsArray());
        await Progress("capitulo-004");
        var fourth = await fixture.Reader.GetFromJsonAsync<JsonObject>($"/api/entities/{maskedId}");
        Assert.Equal("Íria", fourth!["aliases"]![0]!.GetValue<string>());
        Assert.Contains("Queda da Ponte", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));
        await Progress("capitulo-001");
        var reversed = await fixture.Reader.GetFromJsonAsync<JsonObject>($"/api/entities/{maskedId}");
        Assert.Empty(reversed!["aliases"]!.AsArray());
        Assert.Empty(reversed["relations"]!.AsArray());
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));

        async Task Progress(string slug) => Assert.Equal(HttpStatusCode.OK,
            (await Write(fixture.Reader, $"/api/library/{fixture.BookId}/progress",
                new { currentReadingUnitId = units[slug] }, HttpMethod.Patch)).StatusCode);
    }

    [Fact]
    public async Task Quota_failure_preserves_the_prefix_and_failure_replay_does_not_make_a_second_call()
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.StartAsync();
        var runId = RunId(status);
        status = await fixture.NextAsync(status);
        fixture.Provider.FailOnCall = 2;
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var body = NextBody(status);
        var failed = await Write(fixture.Admin, fixture.Path($"{runId}/next"), body);
        Assert.Equal(HttpStatusCode.TooManyRequests, failed.StatusCode);
        Assert.Equal(2, fixture.Provider.Calls);
        var paused = await fixture.StatusAsync(runId);
        Assert.Equal("Paused", paused["state"]!.GetValue<string>());
        Assert.Equal(1, paused["completedUnits"]!.GetValue<int>());
        var partial = await Read(await fixture.Admin.GetAsync(fixture.Path($"{runId}/result")));
        Assert.Empty(partial["document"]!["entities"]![0]!["aliases"]!.AsArray());
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Write(fixture.Admin, fixture.Path($"{runId}/next"), body)).StatusCode);
        Assert.Equal(2, fixture.Provider.Calls);

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var resumed = await fixture.NextAsync(paused);
        Assert.Equal(2, resumed["completedUnits"]!.GetValue<int>());
        Assert.Equal(3, fixture.Provider.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Minimum_call_interval_blocks_extra_provider_work_without_advancing_the_prefix()
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.NextAsync(await fixture.StartAsync());
        var limited = await Write(fixture.Admin, fixture.Path($"{RunId(status)}/next"), NextBody(status));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(1, (await fixture.StatusAsync(RunId(status)))["completedUnits"]!.GetValue<int>());
    }

    [Fact]
    public async Task Cancellation_stops_an_in_flight_call_and_repeated_requests_do_not_restart_it()
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.StartAsync();
        var runId = RunId(status);
        fixture.Provider.BlockNext = true;
        var pending = Write(fixture.Admin, fixture.Path($"{runId}/next"), NextBody(status));
        await fixture.Provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fixture.Provider.Calls);
        var concurrent = await Write(fixture.Admin, fixture.Path($"{runId}/next"), NextBody(status));
        Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
        var cancelled = await Write(fixture.Admin, fixture.Path($"{runId}/cancel"), new { });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await Read(cancelled))["state"]!.GetValue<string>());
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Write(fixture.Admin, fixture.Path($"{runId}/cancel"), new { })).StatusCode);
        var stopped = await fixture.StatusAsync(runId);
        Assert.Equal("Cancelled", stopped["state"]!.GetValue<string>());
        Assert.Equal(0, stopped["completedUnits"]!.GetValue<int>());
        var retry = await Write(fixture.Admin, fixture.Path($"{runId}/next"), NextBody(stopped));
        Assert.False(retry.IsSuccessStatusCode);
        Assert.Equal(1, fixture.Provider.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Unknown_duplicate_malformed_and_oversized_bodies_are_rejected_before_generation()
    {
        using var fixture = await Fixture.CreateAsync();
        var invalidBodies = new[]
        {
            "{\"sources\":[],\"sources\":[],\"editionConfirmed\":true}",
            "{\"sources\":[],\"editionConfirmed\":true,\"publish\":true}",
            "{\"sources\":null,\"editionConfirmed\":true}",
            "{\"sources\":[",
            "{\"sources\":[],\"editionConfirmed\":\"true\"}"
        };
        foreach (var body in invalidBodies)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await Send(fixture.Admin, fixture.Path(), new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(fixture.Admin, fixture.Path("parse"), new StringContent(
                "{\"text\":\"one\",\"text\":\"two\"}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(fixture.Admin, fixture.Path("parse"), new { text = "text", instructions = "publish" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await Send(fixture.Admin, fixture.Path(), new StringContent("text", Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await Send(fixture.Admin, fixture.Path("parse"), new StringContent(
                "{\"text\":\"" + new string('x', 2 * 1024 * 1024) + "\"}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(fixture.Admin, fixture.Path(), Request([new("Too much", new string('x', 512 * 1024 + 1))]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Write(fixture.Admin, fixture.Path(), Request(Enumerable.Range(1, 65)
                .Select(index => new GenerationSource($"Capítulo {index}", "Fonte.")).ToArray()))).StatusCode);
        Assert.Equal(0, fixture.Provider.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    private static GenerationStartRequest Request(GenerationSource[]? sources = null) =>
        new(sources ?? Sources, "Fontes fictícias controladas do teste.", "https://example.test/fontes",
            EditionConfirmed: true, IndexComplete: true);
    private static Guid RunId(JsonNode status) => status["runId"]!.GetValue<Guid>();
    private static object NextBody(JsonNode status) => new
    {
        operationId = Guid.NewGuid(), expectedRevision = status["revision"]!.GetValue<int>()
    };
    private static async Task<JsonObject> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text)!.AsObject();
    }
    private static Task<HttpResponseMessage> Write(HttpClient client, string path, object? body = null,
        HttpMethod? method = null) => Send(client, path, body is null ? null : JsonContent.Create(body), method);
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, HttpContent? body,
        HttpMethod? method = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = body };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient OtherAdmin { get; init; }
        public required HttpClient Reader { get; init; }
        public required ControlledProvider Provider { get; init; }
        public required ManualClock Clock { get; init; }
        public required string DirectoryPath { get; init; }
        public Guid BookId { get; init; }
        public Guid ReaderId { get; init; }
        public string Path(string suffix = "", Guid? bookId = null) =>
            $"/api/admin/books/{bookId ?? BookId}/memory-pack/generations" + (suffix.Length == 0 ? "" : $"/{suffix}");
        public string TransferPath(string suffix) => $"/api/admin/books/{BookId}/memory-pack/{suffix}";

        public static async Task<Fixture> CreateAsync(bool enabled = true, IInterceptor? interceptor = null)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mnemora-generation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var dbFile = System.IO.Path.Combine(directory, "test.db");
            var clock = new ManualClock();
            var provider = new ControlledProvider();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                    services.RemoveAll<MnemoraDbContext>();
                    services.AddDbContext<MnemoraDbContext>(options =>
                    {
                        options.UseSqlite($"Data Source={dbFile};Pooling=False");
                        if (interceptor is not null) options.AddInterceptors(interceptor);
                    });
                    services.RemoveAll<IMemoryPackGenerationProvider>();
                    services.AddSingleton<IMemoryPackGenerationProvider>(provider);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                    services.PostConfigure<GenerationOptions>(options =>
                    {
                        options.Enabled = enabled;
                        options.ApiKey = "fake-test-key-no-network";
                        options.Provider = "Groq";
                        options.Model = "openai/gpt-oss-120b";
                    });
                });
            });
            var reader = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var registered = await Write(reader, "/api/auth/register", new
            {
                email = $"generation-reader-{Guid.NewGuid():N}@test.local", password = "StrongTestPass123"
            });
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
            var readerId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var adminEmail = $"generation-admin-{Guid.NewGuid():N}@test.local";
            var otherEmail = $"generation-other-{Guid.NewGuid():N}@test.local";
            Guid bookId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
                Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
                foreach (var email in new[] { adminEmail, otherEmail })
                {
                    var user = new IdentityUser<Guid> { UserName = email, Email = email };
                    Assert.True((await users.CreateAsync(user, "StrongAdminPass123")).Succeeded);
                    Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                }
                var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
                var book = new Book
                {
                    Title = "As Máscaras do Cais", Author = "Equipe Mnemora", Language = "pt-BR",
                    CatalogKind = BookCatalogKind.Curated, ReadingOutlineStatus = EditorialContentStatus.None,
                    LoreStatus = EditorialContentStatus.None, Description = "Metadado não é fonte narrativa."
                };
                db.Books.Add(book);
                db.UserBooks.Add(new UserBook { UserId = readerId, BookId = book.Id, Status = UserBookStatus.Reading });
                await db.SaveChangesAsync();
                bookId = book.Id;
            }
            var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var other = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            Assert.Equal(HttpStatusCode.OK, (await Write(admin, "/api/auth/login", new
                { email = adminEmail, password = "StrongAdminPass123" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Write(other, "/api/auth/login", new
                { email = otherEmail, password = "StrongAdminPass123" })).StatusCode);
            return new Fixture { Factory = factory, Admin = admin, OtherAdmin = other, Reader = reader,
                Provider = provider, Clock = clock, DirectoryPath = directory, BookId = bookId, ReaderId = readerId };
        }

        public async Task<JsonObject> StartAsync(GenerationStartRequest? request = null)
        {
            var response = await Write(Admin, Path(), request ?? Request());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            return await Read(response);
        }
        public async Task<JsonObject> NextAsync(JsonObject status)
        {
            var response = await Write(Admin, Path($"{RunId(status)}/next"), NextBody(status));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await Read(response);
        }
        public async Task<JsonObject> StatusAsync(Guid runId)
        {
            var response = await Admin.GetAsync(Path(runId.ToString()));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await Read(response);
        }
        public async Task<JsonObject> CompleteAsync(JsonObject status)
        {
            var runId = RunId(status);
            var steps = status["totalUnits"]!.GetValue<int>() - status["completedUnits"]!.GetValue<int>();
            for (var index = 0; index < steps; index++)
            {
                Clock.Advance(TimeSpan.FromSeconds(61));
                status = await NextAsync(status);
            }
            Assert.Equal(status["totalUnits"]!.GetValue<int>(), status["completedUnits"]!.GetValue<int>());
            var response = await Admin.GetAsync(Path($"{runId}/result"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            return await Read(response);
        }
        public async Task AssertNoEditorialContentAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            Assert.Empty(await db.ReadingUnits.ToListAsync());
            Assert.Empty(await db.LoreEntities.ToListAsync());
            Assert.Empty(await db.LoreFacts.ToListAsync());
            Assert.Empty(await db.EntityAliases.ToListAsync());
            Assert.Empty(await db.LoreRelations.ToListAsync());
        }
        public async Task<string> SnapshotAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            return JsonSerializer.Serialize(new
            {
                books = await db.Books.AsNoTracking().OrderBy(book => book.Id).ToListAsync(),
                units = await db.ReadingUnits.AsNoTracking().OrderBy(unit => unit.Id).ToListAsync(),
                entities = await db.LoreEntities.AsNoTracking().OrderBy(entity => entity.Id).ToListAsync(),
                facts = await db.LoreFacts.AsNoTracking().OrderBy(fact => fact.Id).ToListAsync(),
                aliases = await db.EntityAliases.AsNoTracking().OrderBy(alias => alias.Id).ToListAsync(),
                relations = await db.LoreRelations.AsNoTracking().OrderBy(relation => relation.Id).ToListAsync(),
                library = await db.UserBooks.AsNoTracking().OrderBy(book => book.Id).ToListAsync()
            });
        }
        public async Task PublishAsync()
        {
            Assert.Equal(HttpStatusCode.OK, (await Write(Admin, TransferPath("publish-outline"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Write(Admin, TransferPath("publish-lore"))).StatusCode);
        }
        public async Task<Dictionary<string, Guid>> UnitIdsAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<MnemoraDbContext>().ReadingUnits
                .Where(unit => unit.BookId == BookId).ToDictionaryAsync(unit => unit.Slug, unit => unit.Id);
        }
        public void Dispose()
        {
            Admin.Dispose(); OtherAdmin.Dispose(); Reader.Dispose(); Factory.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    private sealed class ControlledProvider : IMemoryPackGenerationProvider
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public int? FailOnCall { get; set; }
        public bool BlockNext { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int EstimateInputTokens(ChapterInput input) => 100;

        public async Task<ExtractionResult> ExtractAsync(ChapterInput input, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            if (BlockNext)
            {
                BlockNext = false;
                Started.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            if (call == FailOnCall)
                throw new GenerationException("provider_rate_limited", "Quota fictícia para testar o prefixo.", 429, 60);
            cancellationToken.ThrowIfCancellationRequested();
            var entities = new List<GenerationNewEntity>();
            var facts = new List<GenerationFact>();
            var aliases = new List<GenerationAlias>();
            var relations = new List<GenerationRelation>();
            string Known(string name) => input.KnownEntities.Single(entity => entity.Name == name).Slug;
            if (input.UnitSlug == "capitulo-001")
            {
                entities.Add(new("mask", "Character", "A mascarada", "A mascarada carrega um mapa dobrado.",
                    [Citation("p1", "A mascarada carrega um mapa dobrado.")]));
                entities.Add(new("lia", "Character", "Lia", "Lia trabalha no cais",
                    [Citation("p2", "Lia trabalha no cais")]));
                facts.Add(new("mask", "Carrega um mapa dobrado.", "mapa dobrado", "Description",
                    [Citation("p1", "A mascarada carrega um mapa dobrado.")]));
                facts.Add(new("lia", "Lia suspeita que A mascarada levou o sino.", null, "Background",
                    [Citation("p2", "suspeita que A mascarada levou o sino, mas não tem prova.")]));
            }
            else if (input.UnitSlug == "capitulo-002")
                facts.Add(new(Known("A mascarada"), "Carrega um mapa dobrado.", "mapa dobrado", "Description",
                    [Citation("p1", "A mascarada carrega um mapa dobrado.")]));
            else if (input.UnitSlug == "capitulo-003")
                relations.Add(new(Known("Lia"), Known("A mascarada"), "ColaboraCom", "Firmam um acordo de colaboração.",
                    [Citation("p1", "Lia e A mascarada firmam um acordo de colaboração.")]));
            else if (input.UnitSlug == "capitulo-004")
            {
                entities.Add(new("old-event", "Event", "Queda da Ponte de Sal", null,
                    [Citation("p2", "O diário relata a Queda da Ponte de Sal, ocorrida sete anos antes.")]));
                aliases.Add(new(Known("A mascarada"), "Íria", [Citation("p1", "Meu nome é Íria")]));
                facts.Add(new("old-event", "A ponte caiu sete anos antes do registro lido agora.", "sete anos antes", "Event",
                    [Citation("p2", "O diário relata a Queda da Ponte de Sal, ocorrida sete anos antes.")]));
            }
            return new(new(entities, facts, aliases, relations, []), 75, 125);
        }
        private static GenerationEvidence Citation(string paragraph, string quote) =>
            new(paragraph, quote, "Fonte fictícia explícita da unidade atual.");
    }

    private sealed class PauseBooksRead : DbCommandInterceptor
    {
        private int armed;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public void Disarm() => Interlocked.Exchange(ref armed, 0);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Books\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Started.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
