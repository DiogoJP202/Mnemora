using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackGeneratedProjectionTests
{
    [Fact]
    public async Task Generated_v1_roundtrips_as_draft_and_future_identity_disappears_on_rewind()
    {
        var generated = Generate(4);
        await using var fixture = await Fixture.CreateAsync(generated);
        Assert.Equal(EditorialContentStatus.Draft, fixture.Book.LoreStatus);
        Assert.Null(await fixture.Reader.ScopeAsync(Guid.NewGuid(), fixture.Book.Id));

        var mask = await fixture.Db.LoreEntities.SingleAsync(x => x.Name == "Mascarada");
        var before = fixture.Scope(3);
        var detail = await fixture.Reader.EntityAsync(before, mask.Id);
        Assert.NotNull(detail);
        Assert.Equal("Mascarada entrega a carta a Lia.", detail.Entity.ShortDescription);
        Assert.Empty(detail.Aliases);
        Assert.DoesNotContain(detail.Facts, x => x.Content.Contains("Íria"));
        Assert.Single(detail.Relations);
        Assert.Empty(await fixture.Reader.RecallAsync(before, "Iria"));
        Assert.Empty(await fixture.Reader.TimelineAsync(before));

        detail = await fixture.Reader.EntityAsync(fixture.Scope(4), mask.Id);
        Assert.NotNull(detail);
        Assert.Contains("Íria", detail.Aliases);
        Assert.Single(await fixture.Reader.RecallAsync(fixture.Scope(4), "Iria"));
        Assert.Single(await fixture.Reader.TimelineAsync(fixture.Scope(4)));
        Assert.Null((await fixture.Reader.TimelineAsync(fixture.Scope(4)))[0].ChronologyIndex);

        detail = await fixture.Reader.EntityAsync(fixture.Scope(1), mask.Id);
        Assert.NotNull(detail);
        Assert.Empty(detail.Aliases);
        Assert.Empty(detail.Relations);
        Assert.Empty(await fixture.Reader.RecallAsync(fixture.Scope(1), "Iria"));
        Assert.Empty(await fixture.Reader.EntitiesAsync(fixture.Scope(1), query: "Íria"));
        Assert.Empty(await fixture.Reader.TimelineAsync(fixture.Scope(1)));
        Assert.Empty(await fixture.Reader.EntitiesAsync(fixture.Scope(null)));
        var repeat = await fixture.Transfer.ImportAsync(fixture.Book.Id, generated);
        Assert.True(repeat!.Validation.AlreadyImported);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Full_generation_and_prefix_have_identical_visible_projections(int through)
    {
        await using var full = await Fixture.CreateAsync(Generate(4));
        await using var prefix = await Fixture.CreateAsync(Generate(through));
        Assert.Equal(await Visible(full, through), await Visible(prefix, through));
    }

    private static async Task<string> Visible(Fixture fixture, int through)
    {
        var scope = fixture.Scope(through);
        var items = new List<object>();
        foreach (var summary in await fixture.Reader.EntitiesAsync(scope))
        {
            var detail = (await fixture.Reader.EntityAsync(scope, summary.Id))!;
            items.Add(new
            {
                summary.Name, summary.Type, summary.ShortDescription, summary.ImageUrl, summary.MemoryHint,
                detail.Aliases,
                Facts = detail.Facts.Select(x => new { x.Content, x.Type, x.MemoryHint }).OrderBy(x => x.Content),
                Relations = detail.Relations.Select(x => new { x.SourceName, x.TargetName, x.RelationType, x.Label })
                    .OrderBy(x => x.SourceName).ThenBy(x => x.TargetName)
            });
        }
        return JsonSerializer.Serialize(new
        {
            Entities = items,
            Recall = (await fixture.Reader.RecallAsync(scope, "Iria")).Select(x => x.Name),
            Timeline = (await fixture.Reader.TimelineAsync(scope)).Select(x => new { x.Entity.Name, x.ChronologyIndex })
        });
    }

    private static MemoryPackDocument Generate(int through)
    {
        var sources = new GenerationSource[]
        {
            new("A carta", "Mascarada entrega a carta a Lia."),
            new("O mapa", "Lia guarda a carta."),
            new("O acordo", "Mascarada e Lia firmam uma aliança."),
            new("A identidade", "Mascarada revela o nome Íria. A Ponte caiu sete anos antes.")
        };
        var assembler = new MemoryPackGenerationAssembler();
        var document = assembler.Create(new("As Máscaras do Cais", "Equipe Mnemora"),
            new(sources, "Material fictício original", EditionConfirmed: true, IndexComplete: true));
        for (var index = 0; index < through; index++)
        {
            var input = assembler.BuildInput(document, sources[index], index);
            GenerationEvidence[] Evidence(string quote) => [new("p1", quote, "A fonte deste capítulo declara a informação.")];
            ChapterExtraction extraction;
            if (index == 0)
                extraction = new(
                    [new("mask", "Character", "Mascarada", sources[0].Summary, Evidence(sources[0].Summary)),
                     new("lia", "Character", "Lia", null, Evidence(sources[0].Summary))],
                    [new("mask", sources[0].Summary, "entrega a carta", "Description", Evidence(sources[0].Summary))], [], [], []);
            else if (index == 2)
                extraction = new([], [], [],
                    [new(document.Entities.Single(x => x.Name == "Mascarada").Slug,
                        document.Entities.Single(x => x.Name == "Lia").Slug, "Alliance",
                        "Firmaram uma aliança.", Evidence(sources[index].Summary))], []);
            else if (index == 3)
            {
                var mask = document.Entities.Single(x => x.Name == "Mascarada").Slug;
                extraction = new(
                    [new("bridge", "Event", "Ponte", "A Ponte caiu sete anos antes.", Evidence("A Ponte caiu sete anos antes."))],
                    [new(mask, "Mascarada revela o nome Íria.", null, "Title", Evidence("Mascarada revela o nome Íria."))],
                    [new(mask, "Íria", Evidence("Mascarada revela o nome Íria."))], [], []);
            }
            else extraction = new([], [], [], [], []);
            document = assembler.Apply(document, input, extraction).Document;
        }
        return document;
    }

    private sealed class Fixture(SqliteConnection connection, MnemoraDbContext db, Book book) : IAsyncDisposable
    {
        public MnemoraDbContext Db { get; } = db;
        public Book Book { get; } = book;
        public MemoryPackTransfer Transfer { get; } = new(db);
        public KnowledgeReader Reader { get; } = new(db);
        public KnowledgeScope Scope(int? order) => new(Guid.Empty, Book.Id, order);
        public static async Task<Fixture> CreateAsync(MemoryPackDocument document)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MnemoraDbContext(new DbContextOptionsBuilder<MnemoraDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var book = new Book { Title = document.Book.Title, Author = document.Book.Author,
                ReadingOutlineStatus = EditorialContentStatus.Draft, LoreStatus = EditorialContentStatus.Draft };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            var fixture = new Fixture(connection, db, book);
            var outcome = await fixture.Transfer.ImportAsync(book.Id, document);
            Assert.NotNull(outcome);
            Assert.True(outcome.Validation.IsValid, string.Join("; ", outcome.Validation.Issues.Select(x => x.Message)));
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
