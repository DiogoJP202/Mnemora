using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed class SourcePreparationEndpointTests
{
    [Fact]
    public async Task Missing_language_is_inferred_from_portuguese_title_and_foreign_source_warning_is_grouped()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Title = "Harry Potter e a Ordem da Fênix";
            book.Language = null;
            await db.SaveChangesAsync();
        });
        var availability = await Read(await fixture.Admin.GetAsync(fixture.GenerationPath("availability")));
        Assert.Equal("pt-BR", availability["automaticSources"]!["targetLanguage"]!.GetValue<string>());
        Assert.Equal("Title", availability["automaticSources"]!["languageSource"]!.GetValue<string>());
        var status = await fixture.CollectAsync();
        Assert.Equal("pt-BR", status["targetLanguage"]!.GetValue<string>());
        Assert.True(status["languageInferred"]!.GetValue<bool>());
        Assert.Equal("Title", status["languageSource"]!.GetValue<string>());
        Assert.Equal("pt", fixture.Sources.LastChapterIndex!.PreferredLanguage);
        Assert.Equal("Capítulo 1", status["units"]![0]!["title"]!.GetValue<string>());
        Assert.Equal("en", status["units"]![0]!["sourceLanguage"]!.GetValue<string>());
        Assert.Single(status["diagnostics"]!.AsArray(), item => item!.GetValue<string>().Contains("Algumas sinopses"));
        Assert.DoesNotContain(status["diagnostics"]!.AsArray(), item => item!.GetValue<string>().Contains("Capítulo 1: fonte em"));
        await fixture.WithDb(async db => Assert.Null((await db.Books.SingleAsync(book => book.Id == fixture.BookId)).Language));
        Assert.Equal(0, fixture.Generation.Calls);
    }

    [Fact]
    public async Task Explicit_edition_language_wins_over_portuguese_title_without_a_foreign_source_warning()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Language = "en";
            await db.SaveChangesAsync();
        });
        var status = await fixture.CollectAsync();
        Assert.Equal("en", status["targetLanguage"]!.GetValue<string>());
        Assert.False(status["languageInferred"]!.GetValue<bool>());
        Assert.Equal("Metadata", status["languageSource"]!.GetValue<string>());
        Assert.Equal("en", fixture.Sources.LastChapterIndex!.PreferredLanguage);
        Assert.DoesNotContain(status["diagnostics"]!.AsArray(), item => item!.GetValue<string>().Contains("Algumas sinopses"));
        Assert.Equal(0, fixture.Generation.Calls);
    }

    [Theory]
    [InlineData("Harry Potter e a Ordem da Fênix")]
    [InlineData("Harry Potter and the Order of the Phoenix")]
    public async Task Fifth_book_saved_in_the_catalog_starts_its_own_source_collection_without_generation(string title)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Title = title;
            await db.SaveChangesAsync();
        });

        var status = await fixture.StartAsync();

        Assert.Equal("Collecting", status["state"]!.GetValue<string>());
        Assert.Equal(new[] { "hp5" }, fixture.Sources.RequestedKeys);
        Assert.Equal(1, fixture.Sources.IndexCalls);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Automatic_source_availability_recognizes_the_fifth_book_without_any_provider_call()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Title = "Harry Potter e a Ordem da Fênix";
            await db.SaveChangesAsync();
        });

        var response = await fixture.Admin.GetAsync(fixture.GenerationPath("availability"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var availability = (await Read(response))["automaticSources"]!;
        Assert.True(availability["supported"]!.GetValue<bool>());
        Assert.Contains(availability["supportedTitles"]!.AsArray(),
            title => title!.GetValue<string>() == "Harry Potter e a Ordem da Fênix");
        Assert.Equal(0, fixture.Sources.IndexCalls);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Unsupported_work_is_identified_before_collection_and_never_reaches_a_provider()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Title = "O mapa fictício de Mira";
            book.Author = "Uma autora de teste";
            await db.SaveChangesAsync();
        });

        var response = await fixture.Admin.GetAsync(fixture.GenerationPath("availability"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var availability = (await Read(response))["automaticSources"]!;
        Assert.False(availability["supported"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(availability["reason"]!.GetValue<string>()));
        Assert.NotEmpty(availability["supportedTitles"]!.AsArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Write(fixture.Admin, fixture.Path())).StatusCode);
        Assert.Equal(0, fixture.Sources.IndexCalls);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Authentication_admin_and_csrf_apply_before_any_source_request()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Factory.CreateClient();
        foreach (var route in new[] { fixture.Path("current"), fixture.Path(Guid.NewGuid().ToString()) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Reader.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(fixture.Path(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(fixture.Reader, fixture.Path())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Admin.PostAsync(fixture.Path(), null)).StatusCode);
        Assert.Equal(0, fixture.Sources.IndexCalls);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
    }

    [Fact]
    public async Task Another_admin_cannot_read_step_generate_or_cancel_the_preparation()
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.StartAsync();
        var id = PreparationId(status);
        var denied = await fixture.OtherAdmin.GetAsync(fixture.Path(id.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
        foreach (var suffix in new[] { "next", "generate", "cancel" })
        {
            var body = suffix switch { "next" => (object)Operation(status), "generate" => GenerateOperation(status), _ => null };
            Assert.Equal(HttpStatusCode.NotFound,
                (await Write(fixture.OtherAdmin, fixture.Path($"{id}/{suffix}"), body)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.OtherAdmin.GetAsync(fixture.Path("current"))).StatusCode);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Repeated_operation_returns_the_recorded_result_and_stale_revisions_do_not_collect_again()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.StartAsync();
        var id = PreparationId(initial);
        var operation = Operation(initial);
        var first = await Write(fixture.Admin, fixture.Path($"{id}/next"), operation);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var collected = await Read(first);
        var repeat = await Write(fixture.Admin, fixture.Path($"{id}/next"), operation);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(collected.ToJsonString(), (await Read(repeat)).ToJsonString());
        Assert.Equal(1, fixture.Sources.ChapterCalls);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(initial))).StatusCode);
        var reusedOperation = new { operationId = operation.OperationId, expectedRevision = Revision(collected) };
        Assert.Equal(HttpStatusCode.Conflict,
            (await Write(fixture.Admin, fixture.Path($"{id}/next"), reusedOperation)).StatusCode);
        Assert.Equal(1, fixture.Sources.ChapterCalls);
        var current = await fixture.Admin.GetAsync(fixture.Path("current"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(id, PreparationId(await Read(current)));
        await fixture.AssertNoEditorialContentAsync();
    }

    [Theory]
    [InlineData("{\"operationId\":\"00000000-0000-0000-0000-000000000000\",\"expectedRevision\":0}")]
    [InlineData("{\"operationId\":\"85dd9a3e-f7c4-4fe5-bad5-fd5ae318ea91\",\"expectedRevision\":-1}")]
    [InlineData("{\"operationId\":\"85dd9a3e-f7c4-4fe5-bad5-fd5ae318ea91\",\"expectedRevision\":0,\"url\":\"http://127.0.0.1/private\"}")]
    [InlineData("{\"operationId\":\"85dd9a3e-f7c4-4fe5-bad5-fd5ae318ea91\",\"expectedRevision\":0,\"expectedRevision\":0}")]
    public async Task Invalid_or_untrusted_step_fields_never_start_collection(string json)
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.StartAsync();
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await Send(fixture.Admin, fixture.Path($"{PreparationId(initial)}/next"), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
    }

    [Fact]
    public async Task Parallel_step_does_not_duplicate_collection_and_cancel_interrupts_the_active_call()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.StartAsync();
        var id = PreparationId(initial);
        fixture.Sources.BlockChapter = true;
        var pending = Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(initial));
        await fixture.Sources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var concurrent = await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(initial));
        Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
        Assert.Equal(1, fixture.Sources.ChapterCalls);
        var cancelled = await Write(fixture.Admin, fixture.Path($"{id}/cancel"));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await Read(cancelled))["state"]!.GetValue<string>());
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccessStatusCode);
        var stopped = await fixture.StatusAsync(id);
        Assert.Equal("Cancelled", stopped["state"]!.GetValue<string>());
        Assert.Equal(0, stopped["completedUnits"]!.GetValue<int>());
        Assert.False((await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(stopped))).IsSuccessStatusCode);
        Assert.Equal(1, fixture.Sources.ChapterCalls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Bibliographic_change_during_http_invalidates_the_result_without_writing_editorial_content()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.StartAsync();
        var id = PreparationId(initial);
        fixture.Sources.BlockChapter = true;
        var pending = Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(initial));
        await fixture.Sources.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Publisher = "Outra edição durante a consulta";
            await db.SaveChangesAsync();
        });
        fixture.Sources.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.Conflict, (await pending.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal(1, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Theory]
    [InlineData("published")]
    [InlineData("unit")]
    [InlineData("changed-edition")]
    public async Task Book_must_remain_empty_unpublished_and_unchanged_until_generation(string change)
    {
        using var fixture = await Fixture.CreateAsync();
        var status = await fixture.CollectAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            if (change == "published") book.LoreStatus = EditorialContentStatus.Published;
            if (change == "changed-edition") book.Isbn13 = "9788532530790";
            if (change == "unit") db.ReadingUnits.Add(new()
            {
                BookId = book.Id, Title = "Índice incluído por outro editor", SafeLabel = "Capítulo 1",
                Slug = "manual-001", Type = ReadingUnitType.Chapter, OrderIndex = 1
            });
            await db.SaveChangesAsync();
        });
        var response = await Write(fixture.Admin,
            fixture.Path($"{PreparationId(status)}/generate"), GenerateOperation(status));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.WithDb(async db => Assert.Empty(await db.LoreEntities.ToListAsync()));
    }

    [Fact]
    public async Task Edition_change_between_the_preparation_gate_and_generation_creation_rejects_handoff()
    {
        var pause = new PauseFourthBookRead();
        using var fixture = await Fixture.CreateAsync(pause);
        var ready = await fixture.CollectAsync();
        pause.Arm();
        var pending = Write(fixture.Admin, fixture.Path($"{PreparationId(ready)}/generate"), GenerateOperation(ready));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Isbn13 = "9788532530790";
            await db.SaveChangesAsync();
        });
        pause.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.Conflict, (await pending.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        var availability = await fixture.Admin.GetAsync(fixture.GenerationPath("availability"));
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var generation = (await Read(availability))["existingSession"];
        Assert.True(generation is null || generation["state"]!.GetValue<string>() == "Cancelled");
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Collected_sources_go_straight_to_generation_with_provenance_and_without_future_context()
    {
        using var fixture = await Fixture.CreateAsync();
        var ready = await fixture.CollectAsync();
        Assert.Equal("Ready", ready["state"]!.GetValue<string>());
        Assert.Equal(2, ready["completedUnits"]!.GetValue<int>());
        Assert.Equal(new[] { "hp1" }, fixture.Sources.RequestedKeys);
        var request = GenerateOperation(ready);
        var generated = await Write(fixture.Admin,
            fixture.Path($"{PreparationId(ready)}/generate"), request);
        Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
        var generation = await Read(generated);
        var runId = generation["runId"]!.GetValue<Guid>();
        var repeat = await Write(fixture.Admin,
            fixture.Path($"{PreparationId(ready)}/generate"), request);
        Assert.True(repeat.IsSuccessStatusCode, await repeat.Content.ReadAsStringAsync());
        Assert.Equal(runId, (await Read(repeat))["runId"]!.GetValue<Guid>());
        Assert.Equal(0, fixture.Generation.Calls);
        var step = await Write(fixture.Admin, fixture.GenerationPath($"{runId}/next"), Operation(generation));
        Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        Assert.Equal(1, fixture.Generation.Calls);
        Assert.Single(fixture.Generation.Inputs);
        Assert.Equal("Mira works at the harbor.", Assert.Single(fixture.Generation.Inputs[0].Paragraphs).Text);
        Assert.Empty(fixture.Generation.Inputs[0].KnownEntities);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(fixture.Generation.Inputs[0]), StringComparison.OrdinalIgnoreCase);
        var resultResponse = await fixture.Admin.GetAsync(fixture.GenerationPath($"{runId}/result"));
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        Assert.Equal("no-store", resultResponse.Headers.CacheControl?.ToString());
        var result = await Read(resultResponse);
        var sources = result["report"]!["sources"]!.AsArray();
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => source!["revisionId"]!.GetValue<string>() == "chapter-revision-1"
            && source["url"]!.GetValue<string>() == "https://en.wikibooks.org/wiki/Test/Chapter_1"
            && source["license"]!.GetValue<string>() == "CC BY-SA 4.0"
            && source["sourceHash"]!.GetValue<string>() == Hash("Mira works at the harbor."));
        Assert.DoesNotContain("revisionId", result["document"]!.ToJsonString(), StringComparison.Ordinal);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Missing_chapter_remains_a_visible_gap_instead_of_invented_narrative()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Sources.MissingOrder = 2;
        var ready = await fixture.CollectAsync();
        Assert.Equal("Partial", ready["state"]!.GetValue<string>());
        Assert.Contains("MissingSource", ready.ToJsonString(), StringComparison.Ordinal);
        var response = await Write(fixture.Admin,
            fixture.Path($"{PreparationId(ready)}/generate"), GenerateOperation(ready, indexComplete: false));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var generation = await Read(response);
        var runId = generation["runId"]!.GetValue<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var step = await Write(fixture.Admin, fixture.GenerationPath($"{runId}/next"), Operation(generation));
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
            generation = await Read(step);
        }
        Assert.Equal(1, fixture.Generation.Calls);
        var result = await Read(await fixture.Admin.GetAsync(fixture.GenerationPath($"{runId}/result")));
        Assert.True(result["report"]!["partial"]!.GetValue<bool>());
        Assert.Contains("MissingSource", result["report"]!["units"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Single(result["report"]!["sources"]!.AsArray());
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Provider_failure_preserves_a_collected_prefix_without_automatic_retry()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Sources.FailOrder = 2;
        var initial = await fixture.StartAsync();
        var id = PreparationId(initial);
        var collectedResponse = await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(initial));
        Assert.Equal(HttpStatusCode.OK, collectedResponse.StatusCode);
        var collected = await Read(collectedResponse);
        var response = await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(collected));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(2, fixture.Sources.ChapterCalls);
        var current = await fixture.StatusAsync(id);
        var prefix = current["units"]!.AsArray()[0]!;
        Assert.Equal("Collected", prefix["status"]!.GetValue<string>());
        Assert.Equal(Hash("Mira works at the harbor."), prefix["sourceHash"]!.GetValue<string>());
        fixture.Sources.FailOrder = null;
        var tooSoon = await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(current));
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.NotNull(tooSoon.Headers.RetryAfter);
        Assert.Equal(2, fixture.Sources.ChapterCalls);
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        var resumed = await Write(fixture.Admin, fixture.Path($"{id}/next"), Operation(current));
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal("Ready", (await Read(resumed))["state"]!.GetValue<string>());
        Assert.Equal(3, fixture.Sources.ChapterCalls);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Generation_requires_edition_confirmation_even_when_the_work_is_recognized()
    {
        using var fixture = await Fixture.CreateAsync();
        var ready = await fixture.CollectAsync();
        var operation = Operation(ready);
        var response = await Write(fixture.Admin, fixture.Path($"{PreparationId(ready)}/generate"), new
        {
            operation.OperationId, operation.ExpectedRevision, editionConfirmed = false, indexComplete = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Generation_rejects_client_supplied_sources_and_publication_instructions()
    {
        using var fixture = await Fixture.CreateAsync();
        var ready = await fixture.CollectAsync();
        var operation = Operation(ready);
        var response = await Write(fixture.Admin, fixture.Path($"{PreparationId(ready)}/generate"), new
        {
            operation.OperationId, operation.ExpectedRevision, editionConfirmed = true, indexComplete = true,
            sources = new[] { new { title = "Invented chapter", summary = "Invented fact." } },
            sourceUrl = "http://127.0.0.1/credentials", publish = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Generation.Calls);
        Assert.Equal(2, fixture.Sources.ChapterCalls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Expired_preparation_cannot_be_read_or_reused_to_start_generation()
    {
        using var fixture = await Fixture.CreateAsync();
        var ready = await fixture.CollectAsync();
        fixture.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Admin.GetAsync(fixture.Path(PreparationId(ready).ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Write(fixture.Admin, fixture.Path($"{PreparationId(ready)}/generate"), GenerateOperation(ready))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Admin.GetAsync(fixture.Path("current"))).StatusCode);
        Assert.Equal(0, fixture.Generation.Calls);
        await fixture.AssertNoEditorialContentAsync();
    }

    [Fact]
    public async Task Unrecognized_or_private_book_is_never_sent_to_the_source_provider()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.Title = "Obra sem correspondência com a fonte suportada";
            book.Author = "Outro autor";
            book.ExternalId = null;
            book.Isbn13 = null;
            await db.SaveChangesAsync();
        });
        Assert.False((await Write(fixture.Admin, fixture.Path())).IsSuccessStatusCode);
        Assert.Equal(0, fixture.Sources.IndexCalls);
        await fixture.WithDb(async db =>
        {
            var book = await db.Books.SingleAsync(book => book.Id == fixture.BookId);
            book.CatalogKind = BookCatalogKind.Private;
            book.OwnerUserId = fixture.ReaderId;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await Write(fixture.Admin, fixture.Path())).StatusCode);
        Assert.Equal(0, fixture.Sources.IndexCalls);
    }

    private sealed record OperationRequest(Guid OperationId, int ExpectedRevision);
    private static OperationRequest Operation(JsonNode status) => new(Guid.NewGuid(), Revision(status));
    private static object GenerateOperation(JsonNode status, bool indexComplete = true)
    {
        var operation = Operation(status);
        return new { operation.OperationId, operation.ExpectedRevision, editionConfirmed = true, indexComplete };
    }
    private static int Revision(JsonNode status) => status["revision"]!.GetValue<int>();
    private static Guid PreparationId(JsonNode status) => status["preparationId"]!.GetValue<Guid>();
    private static async Task<JsonObject> Read(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    private static Task<HttpResponseMessage> Write(HttpClient client, string path, object? body = null) =>
        Send(client, path, body is null ? null : JsonContent.Create(body));
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, HttpContent? body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class Fixture : IDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient OtherAdmin { get; init; }
        public required HttpClient Reader { get; init; }
        public required FakeNarrativeProvider Sources { get; init; }
        public required FakeGenerationProvider Generation { get; init; }
        public required ManualClock Clock { get; init; }
        public required string DirectoryPath { get; init; }
        public Guid BookId { get; init; }
        public Guid ReaderId { get; init; }
        public string Path(string suffix = "") => $"/api/admin/books/{BookId}/source-preparations"
            + (suffix.Length == 0 ? "" : $"/{suffix}");
        public string GenerationPath(string suffix) => $"/api/admin/books/{BookId}/memory-pack/generations/{suffix}";

        public static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mnemora-preparation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var database = System.IO.Path.Combine(directory, "test.db");
            var sources = new FakeNarrativeProvider();
            var generation = new FakeGenerationProvider();
            var clock = new ManualClock();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                    services.RemoveAll<MnemoraDbContext>();
                    services.AddDbContext<MnemoraDbContext>(options =>
                    {
                        options.UseSqlite($"Data Source={database};Pooling=False");
                        if (interceptor is not null) options.AddInterceptors(interceptor);
                    });
                    services.RemoveAll<IBookNarrativeSourceProvider>();
                    services.AddSingleton<IBookNarrativeSourceProvider>(sources);
                    services.RemoveAll<IMemoryPackGenerationProvider>();
                    services.AddSingleton<IMemoryPackGenerationProvider>(generation);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                    services.PostConfigure<GenerationOptions>(options =>
                    {
                        options.Enabled = true; options.Provider = "Groq";
                        options.Model = "openai/gpt-oss-120b"; options.ApiKey = "fake-source-test-key";
                        options.MinCallIntervalSeconds = 0;
                    });
                });
            });
            var bookId = Guid.NewGuid();
            Guid readerId = default;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
                if (!await roles.RoleExistsAsync("Admin")) Assert.True((await roles.CreateAsync(new("Admin"))).Succeeded);
                foreach (var name in new[] { "admin", "other", "reader" })
                {
                    var user = new IdentityUser<Guid> { UserName = $"{name}@preparation.test", Email = $"{name}@preparation.test" };
                    Assert.True((await users.CreateAsync(user, "StrongPreparation123")).Succeeded);
                    if (name != "reader") Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                    else readerId = user.Id;
                }
                var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
                db.Books.Add(new()
                {
                    Id = bookId, Title = "Harry Potter e a Pedra Filosofal", Author = "J. K. Rowling",
                    Isbn13 = "9788532530783", ExternalProvider = "OpenLibrary", ExternalId = "OL32172820M",
                    Language = "pt-BR", Publisher = "Edição fictícia de teste", CatalogKind = BookCatalogKind.Imported,
                    ReadingOutlineStatus = EditorialContentStatus.None, LoreStatus = EditorialContentStatus.None
                });
                await db.SaveChangesAsync();
            }
            var admin = factory.CreateClient(); var other = factory.CreateClient(); var reader = factory.CreateClient();
            foreach (var (client, name) in new[] { (admin, "admin"), (other, "other"), (reader, "reader") })
                Assert.Equal(HttpStatusCode.OK, (await Write(client, "/api/auth/login", new
                    { email = $"{name}@preparation.test", password = "StrongPreparation123" })).StatusCode);
            return new() { Factory = factory, Admin = admin, OtherAdmin = other, Reader = reader, Sources = sources,
                Generation = generation, Clock = clock, BookId = bookId, ReaderId = readerId, DirectoryPath = directory };
        }

        public async Task<JsonObject> StartAsync()
        {
            var response = await Write(Admin, Path());
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            return await Read(response);
        }
        public async Task<JsonObject> StatusAsync(Guid id)
        {
            var response = await Admin.GetAsync(Path(id.ToString()));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await Read(response);
        }
        public async Task<JsonObject> CollectAsync()
        {
            var status = await StartAsync();
            for (var index = 0; index < 2; index++)
            {
                var response = await Write(Admin, Path($"{PreparationId(status)}/next"), Operation(status));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                status = await Read(response);
            }
            return status;
        }
        public async Task WithDb(Func<MnemoraDbContext, Task> action)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<MnemoraDbContext>());
        }
        public Task AssertNoEditorialContentAsync() => WithDb(async db =>
        {
            Assert.Empty(await db.ReadingUnits.ToListAsync());
            Assert.Empty(await db.LoreEntities.ToListAsync());
            Assert.Empty(await db.LoreFacts.ToListAsync());
            Assert.Empty(await db.EntityAliases.ToListAsync());
            Assert.Empty(await db.LoreRelations.ToListAsync());
            Assert.Empty(await db.UserBooks.ToListAsync());
        });
        public void Dispose()
        {
            Sources.Release.TrySetResult();
            Admin.Dispose(); OtherAdmin.Dispose(); Reader.Dispose(); Factory.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    // Generate first reads the gate's book, the exporter and full identity. The next
    // book read belongs to generation creation, after the preparation check passed.
    private sealed class PauseFourthBookRead : DbCommandInterceptor
    {
        private int remaining;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref remaining, 4);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Books\"", StringComparison.Ordinal)
                && Volatile.Read(ref remaining) > 0 && Interlocked.Decrement(ref remaining) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FakeNarrativeProvider : IBookNarrativeSourceProvider
    {
        private int indexCalls;
        private int chapterCalls;
        public int IndexCalls => Volatile.Read(ref indexCalls);
        public int ChapterCalls => Volatile.Read(ref chapterCalls);
        public int? MissingOrder { get; set; }
        public int? FailOrder { get; set; }
        public bool BlockChapter { get; set; }
        public List<string> RequestedKeys { get; } = [];
        public NarrativeSourceIndex? LastChapterIndex { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NarrativeSourceIndex> GetIndexAsync(string bookKey, CancellationToken cancellationToken, string? preferredLanguage = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref indexCalls);
            RequestedKeys.Add(bookKey);
            Assert.Contains(bookKey, new[] { "hp1", "hp5" });
            return Task.FromResult(new NarrativeSourceIndex(
                new(bookKey, bookKey == "hp5" ? "Harry Potter and the Order of the Phoenix"
                    : "Harry Potter and the Philosopher's Stone", "Test", "en", "CC BY-SA 4.0"),
                [new(1, "Arrival", "Test/Chapter 1"), new(2, "The map", "Test/Chapter 2")],
                "index-revision", "https://en.wikibooks.org/wiki/Test", []));
        }
        public async Task<CollectedNarrativeSource?> GetChapterAsync(NarrativeSourceIndex index,
            NarrativeChapter chapter, CancellationToken cancellationToken)
        {
            LastChapterIndex = index;
            Interlocked.Increment(ref chapterCalls);
            if (BlockChapter)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (chapter.Order == FailOrder)
                throw new GenerationException("source_rate_limited", "A fonte de teste atingiu sua quota temporária.", 429, 3);
            if (chapter.Order == MissingOrder) return null;
            var text = chapter.Order == 1 ? "Mira works at the harbor." : "Mira carries a secret map.";
            return new(chapter.Order, chapter.Title, text, new(
                $"capitulo-{chapter.Order:000}", "Wikibooks", $"https://en.wikibooks.org/wiki/Test/Chapter_{chapter.Order}",
                $"chapter-revision-{chapter.Order}", $"page-{chapter.Order}", "en", "CC BY-SA 4.0", "Fictional test authors",
                new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), Hash($"raw-{chapter.Order}"), Hash(text)));
        }
    }

    private sealed class FakeGenerationProvider : IMemoryPackGenerationProvider
    {
        public int Calls { get; private set; }
        public List<ChapterInput> Inputs { get; } = [];
        public int EstimateInputTokens(ChapterInput input) => 100;
        public Task<ExtractionResult> ExtractAsync(ChapterInput input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Inputs.Add(input);
            GenerationNewEntity[] entities = input.UnitSlug == "capitulo-001"
                ? [new("mira", "Character", "Mira", "Mira works at the harbor.",
                    [new("p1", "Mira works at the harbor.", "Explicit fictional chapter evidence.")])]
                : [];
            return Task.FromResult(new ExtractionResult(new(entities, [], [], [], []), 100, 100));
        }
    }
}
