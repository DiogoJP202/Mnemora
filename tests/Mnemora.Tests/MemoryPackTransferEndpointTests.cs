using System.Net;
using System.Net.Http.Json;
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
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackTransferEndpointTests
{
    [Fact]
    public async Task Persistence_failure_rolls_back_earlier_batches_and_allows_a_clean_retry()
    {
        var failure = new FailBeforeRelationsSave();
        using var fixture = await Fixture.CreateAsync(failure);
        var before = await fixture.SnapshotAsync();
        failure.Armed = true;
        var failed = await Write(fixture.Admin, fixture.Path("import"), Package());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(failure.Armed);
        Assert.Equal(before, await fixture.SnapshotAsync());

        var retry = await Write(fixture.Admin, fixture.Path("import"), Package());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        AssertCounts((await Read(retry))["counts"]!);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Equal(4, await db.ReadingUnits.CountAsync());
        Assert.Equal(4, await db.LoreEntities.CountAsync());
        Assert.Equal(1, await db.LoreRelations.CountAsync());
    }

    [Fact]
    public async Task Validation_is_read_only_and_import_creates_a_complete_private_draft()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var validation = await Write(fixture.Admin, fixture.Path("validate"), Package());
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        var report = await Read(validation);
        Assert.True(report["isValid"]!.GetValue<bool>());
        Assert.False(report["alreadyImported"]!.GetValue<bool>());
        Assert.Empty(report["issues"]!.AsArray());
        AssertCounts(report["counts"]!);
        Assert.Equal(before, await fixture.SnapshotAsync());

        var imported = await Write(fixture.Admin, fixture.Path("import"), Package());
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var result = await Read(imported);
        Assert.False(result["alreadyImported"]!.GetValue<bool>());
        AssertCounts(result["counts"]!);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var book = await db.Books.SingleAsync(x => x.Id == fixture.BookId);
            Assert.Equal(EditorialContentStatus.Draft, book.ReadingOutlineStatus);
            Assert.Equal(EditorialContentStatus.Draft, book.LoreStatus);
            Assert.Null(book.ReadingOutlinePublishedAt);
            Assert.Null(book.LorePublishedAt);
            Assert.Equal("https://example.test/pacotes/mares", book.ReadingOutlineSourceUrl);
            Assert.Equal("Universo original de teste", book.ReadingOutlineAttribution);
            Assert.Equal("Descrição externa não é conhecimento.", book.Description);
            Assert.Equal("Editora de teste", book.Publisher);
            Assert.Equal("https://example.test/capa.png", book.CoverUrl);
            var library = await db.UserBooks.SingleAsync();
            Assert.Equal(UserBookStatus.Paused, library.Status);
            Assert.Equal(27, library.CurrentPage);
            Assert.Null(library.CurrentReadingUnitId);
            Assert.Equal("Minha nota particular.", (await db.UserNotes.SingleAsync()).Content);
            Assert.Equal(1, await db.MemoryPackRequests.CountAsync());
        }

        foreach (var route in new[] { "reading-units", "entities", "factions", "locations", "timeline", "recall?q=Nara", "review" })
        {
            var response = await fixture.Reader.GetAsync($"/api/books/{fixture.BookId}/{route}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Nara", await response.Content.ReadAsStringAsync());
        }
        Assert.Contains("Minha nota particular.",
            await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/notes"));
    }

    [Fact]
    public async Task Export_roundtrip_uses_slugs_and_never_transfers_internal_ids_or_user_data()
    {
        using var original = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await Write(original.Admin, original.Path("import"), Package())).StatusCode);
        var exportedResponse = await original.Admin.GetAsync(original.Path("export"));
        Assert.Equal(HttpStatusCode.OK, exportedResponse.StatusCode);
        Assert.Equal("no-store", exportedResponse.Headers.CacheControl?.ToString());
        var exported = await Read(exportedResponse);
        Assert.Equal(1, exported["schemaVersion"]!.GetValue<int>());
        AssertNoInternalFields(exported);
        var exportedText = exported.ToJsonString();
        Assert.DoesNotContain(original.BookId.ToString(), exportedText);
        Assert.DoesNotContain(original.ReaderId.ToString(), exportedText);
        Assert.DoesNotContain("Minha nota particular.", exportedText);
        Assert.DoesNotContain("Descrição externa não é conhecimento.", exportedText);
        Assert.Equal("parte-um", exported["readingUnits"]![1]!["parentSlug"]!.GetValue<string>());
        var exportedNara = exported["entities"]!.AsArray().Single(x => x!["slug"]!.GetValue<string>() == "nara");
        Assert.Equal("capitulo-2", exportedNara!["aliases"]![0]!["revealAtUnitSlug"]!.GetValue<string>());

        using var destination = await Fixture.CreateAsync();
        Assert.NotEqual(original.BookId, destination.BookId);
        Assert.Equal(HttpStatusCode.Created,
            (await Write(destination.Admin, destination.Path("import"), exported)).StatusCode);
        var secondExport = await destination.Admin.GetFromJsonAsync<JsonObject>(destination.Path("export"));
        Assert.NotNull(secondExport);
        Assert.True(JsonNode.DeepEquals(exported, secondExport));
        var originalIds = await original.ContentIdsAsync();
        var destinationIds = await destination.ContentIdsAsync();
        Assert.Empty(originalIds.Intersect(destinationIds));
    }

    [Fact]
    public async Task Identical_import_preserves_published_content_progress_notes_and_review_history()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await Write(fixture.Admin, fixture.Path("import"), Package())).StatusCode);
        await fixture.PublishAsync();
        var units = await fixture.UnitIdsAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await Write(fixture.Reader, $"/api/library/{fixture.BookId}/progress", new
            {
                currentReadingUnitId = units["capitulo-3"], currentPage = 51, status = "Reading"
            }, HttpMethod.Patch)).StatusCode);
        Guid entityId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            entityId = await db.LoreEntities.Where(x => x.Slug == "nara").Select(x => x.Id).SingleAsync();
        }
        Assert.Equal(HttpStatusCode.Created,
            (await Write(fixture.Reader, $"/api/books/{fixture.BookId}/notes", new
            {
                content = "Nota ligada à personagem.", entityId, readingUnitId = units["capitulo-3"]
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Write(fixture.Reader, $"/api/books/{fixture.BookId}/review/{entityId}",
                new { remembered = true })).StatusCode);
        var before = await fixture.SnapshotAsync();

        var validation = await Read(await Write(fixture.Admin, fixture.Path("validate"), Package()));
        Assert.True(validation["isValid"]!.GetValue<bool>());
        Assert.True(validation["alreadyImported"]!.GetValue<bool>());
        var repeated = await Write(fixture.Admin, fixture.Path("import"), Package());
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True((await Read(repeated))["alreadyImported"]!.GetValue<bool>());
        Assert.Equal(before, await fixture.SnapshotAsync());

        var reordered = Package();
        foreach (var list in new[] { "readingUnits", "entities", "relations" })
            reordered[list] = new JsonArray(reordered[list]!.AsArray().Reverse()
                .Select(x => x!.DeepClone()).ToArray());
        var reorderedNara = reordered["entities"]!.AsArray().Single(x => x!["slug"]!.GetValue<string>() == "nara")!;
        reorderedNara["facts"] = new JsonArray(reorderedNara["facts"]!.AsArray().Reverse()
            .Select(x => x!.DeepClone()).ToArray());
        reorderedNara["type"] = "character";
        var normalizedRepeat = await Write(fixture.Admin, fixture.Path("import"), reordered);
        Assert.Equal(HttpStatusCode.OK, normalizedRepeat.StatusCode);
        Assert.True((await Read(normalizedRepeat))["alreadyImported"]!.GetValue<bool>());
        Assert.Equal(before, await fixture.SnapshotAsync());

        var different = Package();
        different["entities"]![0]!["name"] = "Nome alterado";
        var rejectedValidation = await Write(fixture.Admin, fixture.Path("validate"), different);
        Assert.Equal(HttpStatusCode.OK, rejectedValidation.StatusCode);
        Assert.False((await Read(rejectedValidation))["isValid"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Write(fixture.Admin, fixture.Path("import"), different)).StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Imported_knowledge_obeys_reveal_boundaries_and_progress_reversal()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await Write(fixture.Admin, fixture.Path("import"), Package())).StatusCode);
        await fixture.PublishAsync();
        var units = await fixture.UnitIdsAsync();
        Guid naraId;
        Guid eventId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            naraId = await db.LoreEntities.Where(x => x.Slug == "nara").Select(x => x.Id).SingleAsync();
            eventId = await db.LoreEntities.Where(x => x.Type == LoreEntityType.Event).Select(x => x.Id).SingleAsync();
        }

        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/entities"));
        await Progress("capitulo-1");
        var before = await fixture.Reader.GetStringAsync($"/api/entities/{naraId}");
        Assert.Contains("Nara", before);
        Assert.Contains("Viajante do porto", before);
        foreach (var future in new[] { "Vigia azul", "selo de prata", "selo lunar", "Guarda das Marés", "protege" })
            Assert.DoesNotContain(future, before);
        var safeUnits = await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/reading-units");
        Assert.Contains("Capítulo 3", safeUnits);
        Assert.DoesNotContain("A revelação do selo", safeUnits);
        Assert.Equal("[]", await Recall("Vigia azul"));
        Assert.Equal("[]", await Recall("selo lunar"));
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Reader.GetAsync($"/api/entities/{eventId}")).StatusCode);

        await Progress("capitulo-2");
        var atBoundary = await fixture.Reader.GetStringAsync($"/api/entities/{naraId}");
        Assert.Contains("Vigia azul", atBoundary);
        Assert.Contains("Guarda das Marés", atBoundary);
        Assert.Contains("protege", atBoundary);
        Assert.DoesNotContain("selo de prata", atBoundary);
        Assert.Contains("Nara", await Recall("Vigia azul"));
        Assert.Contains("Guarda das Marés", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/factions"));
        Assert.Contains("Ponte Velha", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/locations"));
        Assert.Equal("[]", await Recall("selo lunar"));
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));

        await Progress("capitulo-3");
        var after = await fixture.Reader.GetStringAsync($"/api/entities/{naraId}");
        Assert.Contains("selo de prata", after);
        Assert.Contains("selo lunar", after);
        Assert.Contains("Nara", await Recall("selo lunar"));
        Assert.Contains("Abertura do farol", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.Reader.GetAsync($"/api/entities/{eventId}")).StatusCode);

        await Progress("capitulo-1");
        var reversed = await fixture.Reader.GetStringAsync($"/api/entities/{naraId}");
        Assert.DoesNotContain("Vigia azul", reversed);
        Assert.DoesNotContain("selo de prata", reversed);
        Assert.Equal("[]", await Recall("Vigia azul"));
        Assert.Equal("[]", await Recall("selo lunar"));
        Assert.Equal("[]", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/timeline"));
        Assert.DoesNotContain("Guarda das Marés", await fixture.Reader.GetStringAsync($"/api/books/{fixture.BookId}/review"));

        async Task Progress(string slug) => Assert.Equal(HttpStatusCode.OK,
            (await Write(fixture.Reader, $"/api/library/{fixture.BookId}/progress",
                new { currentReadingUnitId = units[slug] }, HttpMethod.Patch)).StatusCode);
        Task<string> Recall(string query) => fixture.Reader.GetStringAsync(
            $"/api/books/{fixture.BookId}/recall?q={Uri.EscapeDataString(query)}");
    }

    [Fact]
    public async Task Invalid_graphs_and_numeric_enums_report_paths_without_partial_persistence()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var cases = new (string Name, Action<JsonObject> Mutate)[]
        {
            ("duplicate unit slug", p => p["readingUnits"]![2]!["slug"] = "capitulo-1"),
            ("duplicate unit order", p => p["readingUnits"]![2]!["orderIndex"] = 10),
            ("unknown parent", p => p["readingUnits"]![1]!["parentSlug"] = "missing"),
            ("cyclic hierarchy", p => p["readingUnits"]![0]!["parentSlug"] = "capitulo-1"),
            ("invalid slug", p => p["entities"]![0]!["slug"] = "Nara com espaço"),
            ("duplicate entity slug", p => p["entities"]![1]!["slug"] = "nara"),
            ("numeric unit enum", p => p["readingUnits"]![1]!["type"] = "1"),
            ("numeric entity enum", p => p["entities"]![0]!["type"] = "0"),
            ("numeric fact enum", p => p["entities"]![0]!["facts"]![0]!["type"] = "0"),
            ("unknown first reveal", p => p["entities"]![0]!["firstKnownAtUnitSlug"] = "missing"),
            ("alias before entity", p => p["entities"]![0]!["aliases"]![0]!["revealAtUnitSlug"] = "parte-um"),
            ("fact before entity", p => p["entities"]![0]!["facts"]![0]!["revealAtUnitSlug"] = "parte-um"),
            ("unknown relation entity", p => p["relations"]![0]!["targetEntitySlug"] = "missing"),
            ("relation before target", p => p["relations"]![0]!["revealAtUnitSlug"] = "capitulo-1"),
            ("unsafe source URL", p => p["source"]!["url"] = "javascript:alert(1)"),
            ("negative importance", p => p["entities"]![0]!["importance"] = -1),
            ("unsupported schema", p => p["schemaVersion"] = 2),
            ("null unit item", p => p["readingUnits"]![1] = null),
            ("null entity item", p => p["entities"]![1] = null),
            ("null alias item", p => p["entities"]![0]!["aliases"]![0] = null),
            ("null fact item", p => p["entities"]![0]!["facts"]![0] = null),
            ("null relation item", p => p["relations"]![0] = null),
            ("null required array", p => p["entities"]![0]!["facts"] = null)
        };
        foreach (var (name, mutate) in cases)
        {
            var invalid = Package();
            mutate(invalid);
            var validated = await Write(fixture.Admin, fixture.Path("validate"), invalid);
            Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
            var report = await Read(validated);
            Assert.False(report["isValid"]!.GetValue<bool>());
            Assert.True(report["issues"]!.AsArray().Count > 0, name);
            Assert.All(report["issues"]!.AsArray(), issue =>
            {
                Assert.False(string.IsNullOrWhiteSpace(issue!["path"]!.GetValue<string>()));
                Assert.False(string.IsNullOrWhiteSpace(issue["message"]!.GetValue<string>()));
            });
            var imported = await Write(fixture.Admin, fixture.Path("import"), invalid);
            Assert.True(imported.StatusCode == HttpStatusCode.UnprocessableEntity,
                $"{name}: expected 422, received {(int)imported.StatusCode}: {await imported.Content.ReadAsStringAsync()}");
            Assert.Equal(before, await fixture.SnapshotAsync());
        }
    }

    [Fact]
    public async Task Edition_reference_must_match_every_identity_field_without_changing_book_metadata()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        foreach (var field in new[] { "title", "author", "externalProvider", "externalId", "isbn10", "isbn13", "language" })
        {
            var wrong = Package();
            wrong["book"]![field] = "Outra edição";
            var validation = await Read(await Write(fixture.Admin, fixture.Path("validate"), wrong));
            Assert.False(validation["isValid"]!.GetValue<bool>());
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await Write(fixture.Admin, fixture.Path("import"), wrong)).StatusCode);
            Assert.Equal(before, await fixture.SnapshotAsync());
        }
        var missingReference = Package();
        missingReference["book"]!.AsObject().Remove("externalId");
        Assert.False((await Read(await Write(fixture.Admin, fixture.Path("validate"), missingReference)))["isValid"]!.GetValue<bool>());
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Collection_and_body_limits_reject_the_whole_package()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var tooManyUnits = Package();
        tooManyUnits["readingUnits"] = new JsonArray(Enumerable.Range(1, 501)
            .Select(index => (JsonNode)new JsonObject
            {
                ["slug"] = $"unit-{index}", ["title"] = $"Unidade {index}",
                ["safeLabel"] = $"Unidade {index}", ["type"] = "Chapter", ["orderIndex"] = index
            }).ToArray());
        var tooManyEntities = Package();
        tooManyEntities["entities"] = new JsonArray(Enumerable.Range(1, 501)
            .Select(index =>
            {
                var entity = Package()["entities"]![0]!.DeepClone();
                entity["slug"] = $"entity-{index}";
                return entity;
            }).ToArray());
        var tooManyAliases = Package();
        tooManyAliases["entities"]![0]!["aliases"] = new JsonArray(Enumerable.Range(1, 21)
            .Select(index => (JsonNode)new JsonObject
            {
                ["alias"] = $"Nome {index}", ["revealAtUnitSlug"] = "capitulo-2"
            }).ToArray());
        var tooManyFacts = Package();
        tooManyFacts["entities"]![0]!["facts"] = new JsonArray(Enumerable.Range(1, 101)
            .Select(index => (JsonNode)new JsonObject
            {
                ["content"] = $"Fato {index}", ["type"] = "Description", ["importance"] = 1,
                ["revealAtUnitSlug"] = "capitulo-1"
            }).ToArray());
        var tooManyRelations = Package();
        tooManyRelations["relations"] = new JsonArray(Enumerable.Range(1, 5001)
            .Select(index =>
            {
                var relation = Package()["relations"]![0]!.DeepClone();
                relation["label"] = $"Relação {index}";
                return relation;
            }).ToArray());
        foreach (var oversized in new[] { tooManyUnits, tooManyEntities, tooManyAliases, tooManyFacts, tooManyRelations })
        {
            var report = await Read(await Write(fixture.Admin, fixture.Path("validate"), oversized));
            Assert.False(report["isValid"]!.GetValue<bool>());
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await Write(fixture.Admin, fixture.Path("import"), oversized)).StatusCode);
            Assert.Equal(before, await fixture.SnapshotAsync());
        }
        var bodyTooLarge = new string(' ', 2 * 1024 * 1024) + Package().ToJsonString();
        foreach (var route in new[] { "validate", "import" })
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
                (await WriteRaw(fixture.Admin, fixture.Path(route), bodyTooLarge)).StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Exact_collection_limits_are_accepted_without_writing_during_validation()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var atLimits = Package();
        for (var index = 5; index <= 500; index++)
        {
            atLimits["readingUnits"]!.AsArray().Add(new JsonObject
            {
                ["slug"] = $"unit-{index}", ["title"] = $"Unidade {index}",
                ["safeLabel"] = $"Unidade {index}", ["type"] = "Chapter", ["orderIndex"] = 30 + index
            });
            atLimits["entities"]!.AsArray().Add(new JsonObject
            {
                ["slug"] = $"entity-{index}", ["name"] = $"Pessoa {index}", ["type"] = "Character",
                ["firstKnownAtUnitSlug"] = "capitulo-1", ["importance"] = 0,
                ["aliases"] = new JsonArray(), ["facts"] = new JsonArray()
            });
        }
        atLimits["entities"]![0]!["aliases"] = new JsonArray(Enumerable.Range(1, 20)
            .Select(index => (JsonNode)new JsonObject
            {
                ["alias"] = $"Nome {index}", ["revealAtUnitSlug"] = "capitulo-2"
            }).ToArray());
        atLimits["entities"]![0]!["facts"] = new JsonArray(Enumerable.Range(1, 100)
            .Select(index => (JsonNode)new JsonObject
            {
                ["content"] = $"Fato {index}", ["type"] = "Description", ["importance"] = 0,
                ["revealAtUnitSlug"] = "capitulo-1"
            }).ToArray());
        var relationTemplate = atLimits["relations"]![0]!.DeepClone();
        atLimits["relations"] = new JsonArray(Enumerable.Range(1, 5000)
            .Select(index =>
            {
                var relation = relationTemplate.DeepClone();
                relation["label"] = $"Relação {index}";
                return relation;
            }).ToArray());
        var report = await Read(await Write(fixture.Admin, fixture.Path("validate"), atLimits));
        Assert.True(report["isValid"]!.GetValue<bool>(), report.ToJsonString());
        Assert.Empty(report["issues"]!.AsArray());
        var counts = report["counts"]!;
        Assert.Equal(500, counts["readingUnits"]!.GetValue<int>());
        Assert.Equal(500, counts["entities"]!.GetValue<int>());
        Assert.Equal(20, counts["aliases"]!.GetValue<int>());
        Assert.Equal(100, counts["facts"]!.GetValue<int>());
        Assert.Equal(5000, counts["relations"]!.GetValue<int>());
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Authorization_private_book_and_csrf_checks_protect_every_transfer_route()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Factory.CreateClient();
        Guid privateBookId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var privateBook = new Book
            {
                Title = "Livro particular", Author = "Leitor", CatalogKind = BookCatalogKind.Private,
                OwnerUserId = fixture.ReaderId, ReadingOutlineStatus = EditorialContentStatus.None,
                LoreStatus = EditorialContentStatus.None
            };
            db.Books.Add(privateBook);
            await db.SaveChangesAsync();
            privateBookId = privateBook.Id;
        }
        foreach (var route in new[] { "export", "validate", "import" })
        {
            var method = route == "export" ? HttpMethod.Get : HttpMethod.Post;
            using var unauthenticated = new HttpRequestMessage(method, fixture.Path(route));
            if (method == HttpMethod.Post) unauthenticated.Content = JsonContent.Create(Package());
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.SendAsync(unauthenticated)).StatusCode);
            var readerResponse = method == HttpMethod.Get
                ? await fixture.Reader.GetAsync(fixture.Path(route))
                : await Write(fixture.Reader, fixture.Path(route), Package());
            Assert.Equal(HttpStatusCode.Forbidden, readerResponse.StatusCode);
            var privatePath = $"/api/admin/books/{privateBookId}/memory-pack/{route}";
            var privateResponse = method == HttpMethod.Get
                ? await fixture.Admin.GetAsync(privatePath)
                : await Write(fixture.Admin, privatePath, Package());
            Assert.Equal(HttpStatusCode.NotFound, privateResponse.StatusCode);
        }
        foreach (var route in new[] { "validate", "import" })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await fixture.Admin.PostAsJsonAsync(fixture.Path(route), Package())).StatusCode);
        }
        await using var verification = fixture.Factory.Services.CreateAsyncScope();
        Assert.Empty(await verification.ServiceProvider.GetRequiredService<MnemoraDbContext>().ReadingUnits.ToListAsync());
    }

    [Fact]
    public async Task Malformed_json_unknown_fields_and_wrong_json_types_are_rejected_without_writes()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var extra = Package();
        extra["userNotes"] = new JsonArray("This must never be imported.");
        var wrongType = Package();
        wrongType["entities"]![0]!["type"] = 0;
        var nestedExtra = Package();
        nestedExtra["entities"]![0]!["id"] = Guid.NewGuid().ToString();
        var numericString = Package();
        numericString["schemaVersion"] = "1";
        var stringOrder = Package();
        stringOrder["readingUnits"]![1]!["orderIndex"] = "10";
        var duplicatedKey = Package().ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        foreach (var json in new[] { "{", "null", extra.ToJsonString(), nestedExtra.ToJsonString(), wrongType.ToJsonString(), numericString.ToJsonString(), stringOrder.ToJsonString(), duplicatedKey })
        {
            foreach (var route in new[] { "validate", "import" })
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await WriteRaw(fixture.Admin, fixture.Path(route), json)).StatusCode);
            Assert.Equal(before, await fixture.SnapshotAsync());
        }
    }

    private static void AssertCounts(JsonNode counts)
    {
        Assert.Equal(4, counts["readingUnits"]!.GetValue<int>());
        Assert.Equal(4, counts["entities"]!.GetValue<int>());
        Assert.Equal(1, counts["aliases"]!.GetValue<int>());
        Assert.Equal(2, counts["facts"]!.GetValue<int>());
        Assert.Equal(1, counts["relations"]!.GetValue<int>());
        Assert.Equal(1, counts["events"]!.GetValue<int>());
    }

    private static void AssertNoInternalFields(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj)
            {
                Assert.DoesNotContain(name, new[] { "id", "bookId", "userId", "ownerUserId", "entityId", "readingUnitId", "createdAt", "updatedAt", "publishedAt", "userNotes", "userBooks", "reviewHistory", "requests" });
                AssertNoInternalFields(value);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array) AssertNoInternalFields(child);
    }

    private static JsonObject Package() => JsonNode.Parse("""
        {
          "schemaVersion": 1,
          "book": { "title": "As Marés da Névoa", "author": "Autora de teste", "externalProvider": "OpenLibrary", "externalId": "OL123M", "isbn10": "0306406152", "isbn13": "9780306406157", "language": "pt-BR" },
          "source": { "url": "https://example.test/pacotes/mares", "attribution": "Universo original de teste" },
          "readingUnits": [
            { "slug": "parte-um", "title": "O porto", "safeLabel": "Parte 1", "type": "Part", "orderIndex": 1 },
            { "slug": "capitulo-1", "parentSlug": "parte-um", "title": "A chegada", "safeLabel": "Capítulo 1", "type": "Chapter", "orderIndex": 10 },
            { "slug": "capitulo-2", "parentSlug": "parte-um", "title": "A guarda", "safeLabel": "Capítulo 2", "type": "Chapter", "orderIndex": 20 },
            { "slug": "capitulo-3", "parentSlug": "parte-um", "title": "A revelação do selo", "safeLabel": "Capítulo 3", "type": "Chapter", "orderIndex": 30 }
          ],
          "entities": [
            { "slug": "nara", "type": "Character", "name": "Nara", "shortDescription": "Uma viajante.", "firstKnownAtUnitSlug": "capitulo-1", "importance": 3,
              "aliases": [{ "alias": "Vigia azul", "revealAtUnitSlug": "capitulo-2" }],
              "facts": [
                { "content": "Viajante do porto.", "memoryHint": "bolsa de mapas", "type": "Description", "importance": 1, "revealAtUnitSlug": "capitulo-1" },
                { "content": "Guarda o selo de prata.", "memoryHint": "selo lunar", "type": "Background", "importance": 2, "revealAtUnitSlug": "capitulo-3" }
              ]
            },
            { "slug": "guarda", "type": "Faction", "name": "Guarda das Marés", "firstKnownAtUnitSlug": "capitulo-2", "importance": 2, "aliases": [], "facts": [] },
            { "slug": "ponte", "type": "Location", "name": "Ponte Velha", "firstKnownAtUnitSlug": "capitulo-2", "importance": 1, "aliases": [], "facts": [] },
            { "slug": "farol", "type": "Event", "name": "Abertura do farol", "firstKnownAtUnitSlug": "capitulo-3", "importance": 1, "chronologyIndex": 5, "aliases": [], "facts": [] }
          ],
          "relations": [{ "sourceEntitySlug": "nara", "targetEntitySlug": "guarda", "relationType": "protege", "label": "Um acordo no porto", "revealAtUnitSlug": "capitulo-2" }]
        }
        """)!.AsObject();

    private static async Task<JsonObject> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    private static Task<HttpResponseMessage> Write(HttpClient client, string path, object? body = null,
        HttpMethod? method = null) => Send(client, path,
            body is null ? null : JsonContent.Create(body), method ?? HttpMethod.Post);

    private static Task<HttpResponseMessage> WriteRaw(HttpClient client, string path, string json) =>
        Send(client, path, new StringContent(json, Encoding.UTF8, "application/json"), HttpMethod.Post);

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path,
        HttpContent? content, HttpMethod method)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Reader { get; init; }
        public Guid BookId { get; init; }
        public Guid ReaderId { get; init; }
        public string Path(string operation) => $"/api/admin/books/{BookId}/memory-pack/{operation}";

        public static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var dbFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mnemora-transfer-{Guid.NewGuid():N}.db");
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                    services.RemoveAll<MnemoraDbContext>();
                    services.AddDbContext<MnemoraDbContext>(options =>
                    {
                        options.UseSqlite($"Data Source={dbFile}");
                        if (interceptor is not null) options.AddInterceptors(interceptor);
                    });
                });
            });
            var reader = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var registered = await Write(reader, "/api/auth/register", new
            {
                email = $"transfer-reader-{Guid.NewGuid():N}@test.local", password = "StrongTestPass123"
            });
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
            var readerId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var adminEmail = $"transfer-admin-{Guid.NewGuid():N}@test.local";
            Guid bookId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
                Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
                var user = new IdentityUser<Guid> { UserName = adminEmail, Email = adminEmail };
                Assert.True((await users.CreateAsync(user, "StrongAdminPass123")).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
                var book = new Book
                {
                    Title = "As Marés da Névoa", Author = "Autora de teste", CatalogKind = BookCatalogKind.Imported,
                    ExternalProvider = "OpenLibrary", ExternalId = "OL123M", Isbn10 = "0306406152",
                    Isbn13 = "9780306406157", Language = "pt-BR", ReadingOutlineStatus = EditorialContentStatus.None,
                    LoreStatus = EditorialContentStatus.None, Description = "Descrição externa não é conhecimento.",
                    Publisher = "Editora de teste", CoverUrl = "https://example.test/capa.png"
                };
                db.Books.Add(book);
                db.UserBooks.Add(new UserBook
                {
                    UserId = readerId, BookId = book.Id, Status = UserBookStatus.Paused, CurrentPage = 27
                });
                db.UserNotes.Add(new UserNote { UserId = readerId, BookId = book.Id, Content = "Minha nota particular." });
                db.MemoryPackRequests.Add(new MemoryPackRequest { UserId = readerId, BookId = book.Id });
                await db.SaveChangesAsync();
                bookId = book.Id;
            }
            var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            Assert.Equal(HttpStatusCode.OK, (await Write(admin, "/api/auth/login", new
            {
                email = adminEmail, password = "StrongAdminPass123"
            })).StatusCode);
            return new Fixture { Factory = factory, Admin = admin, Reader = reader, BookId = bookId, ReaderId = readerId };
        }

        public async Task PublishAsync()
        {
            Assert.Equal(HttpStatusCode.OK,
                (await Write(Admin, Path("publish-outline"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await Write(Admin, Path("publish-lore"))).StatusCode);
        }

        public async Task<Dictionary<string, Guid>> UnitIdsAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<MnemoraDbContext>().ReadingUnits
                .Where(x => x.BookId == BookId).ToDictionaryAsync(x => x.Slug, x => x.Id);
        }

        public async Task<HashSet<Guid>> ContentIdsAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            var units = await db.ReadingUnits.Select(x => x.Id).ToListAsync();
            var entities = await db.LoreEntities.Select(x => x.Id).ToListAsync();
            var aliases = await db.EntityAliases.Select(x => x.Id).ToListAsync();
            var facts = await db.LoreFacts.Select(x => x.Id).ToListAsync();
            var relations = await db.LoreRelations.Select(x => x.Id).ToListAsync();
            return units.Concat(entities).Concat(aliases).Concat(facts).Concat(relations).ToHashSet();
        }

        public async Task<string> SnapshotAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            return JsonSerializer.Serialize(new
            {
                books = await db.Books.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                units = await db.ReadingUnits.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                entities = await db.LoreEntities.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                aliases = await db.EntityAliases.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                facts = await db.LoreFacts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                relations = await db.LoreRelations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                library = await db.UserBooks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                notes = await db.UserNotes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                requests = await db.MemoryPackRequests.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                review = await db.UserRecallActivities.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
            });
        }

        public void Dispose()
        {
            Admin.Dispose();
            Reader.Dispose();
            Factory.Dispose();
        }
    }

    private sealed class FailBeforeRelationsSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LoreRelation>()
                .Any(x => x.State == EntityState.Added))
            {
                Armed = false;
                throw new InvalidOperationException("Falha de persistência induzida para verificar rollback.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
