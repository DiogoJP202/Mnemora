using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class KnowledgeReaderTests
{
    [Theory]
    [InlineData(null, 10, false)]
    [InlineData(20, 10, true)]
    [InlineData(20, 20, true)]
    [InlineData(20, 40, false)]
    public void Visibility_has_inclusive_boundary(int? progress, int reveal, bool expected) =>
        Assert.Equal(expected, KnowledgeVisibility.CanSee(progress, reveal));

    [Fact]
    public async Task Sql_queries_never_match_future_alias_facts_entities_relations_or_events()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MnemoraDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new MnemoraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var book = new Book { Title = "O Arquivo da Neblina", Author = "Equipe Mnemora" };
        db.Users.Add(new IdentityUser<Guid> { Id = userId, UserName = "reader@test.local" });
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var units = new[] { Unit(book.Id, 10), Unit(book.Id, 20), Unit(book.Id, 40) };
        db.ReadingUnits.AddRange(units);
        await db.SaveChangesAsync();
        var known = Entity(book.Id, units[0].Id, "Nara", LoreEntityType.Character);
        var future = Entity(book.Id, units[2].Id, "Iria", LoreEntityType.Character);
        var eventKnown = Entity(book.Id, units[1].Id, "A ponte", LoreEntityType.Event);
        var eventFuture = Entity(book.Id, units[2].Id, "O farol", LoreEntityType.Event);
        db.LoreEntities.AddRange(known, future, eventKnown, eventFuture);
        db.UserBooks.Add(new UserBook
        {
            UserId = userId, BookId = book.Id,
            Status = UserBookStatus.Reading,
            CurrentReadingUnitId = units[1].Id
        });
        await db.SaveChangesAsync();

        db.EntityAliases.AddRange(
            new EntityAlias { EntityId = known.Id, Alias = "Exploradora", RevealAtUnitId = units[0].Id },
            new EntityAlias { EntityId = known.Id, Alias = "Rainha oculta", RevealAtUnitId = units[2].Id });
        db.LoreFacts.AddRange(
            new LoreFact { EntityId = known.Id, Content = "Nara cruza a ponte.", MemoryHint = "A viajante da ponte.", RevealAtUnitId = units[0].Id },
            new LoreFact { EntityId = known.Id, Content = "Nara encontra a chave.", RevealAtUnitId = units[1].Id },
            new LoreFact { EntityId = known.Id, Content = "Segredo do farol.", MemoryHint = "Rainha oculta.", RevealAtUnitId = units[2].Id });
        db.LoreRelations.Add(new LoreRelation
        {
            BookId = book.Id, SourceEntityId = known.Id, TargetEntityId = future.Id,
            RelationType = "Knows", RevealAtUnitId = units[2].Id
        });
        await db.SaveChangesAsync();

        var reader = new KnowledgeReader(db);
        var scope = await reader.ScopeAsync(userId, book.Id);
        Assert.NotNull(scope);
        Assert.Equal(20, scope.CurrentOrder);
        var detail = await reader.EntityAsync(scope, known.Id);
        Assert.NotNull(detail);
        Assert.Equal(2, detail.Facts.Count);
        Assert.Single(detail.Aliases);
        Assert.Empty(detail.Relations);
        Assert.Null(await reader.EntityAsync(scope, future.Id));
        Assert.Empty(await reader.EntitiesAsync(scope, query: "Rainha oculta"));
        Assert.Empty(await reader.EntitiesAsync(scope, query: "Segredo do farol"));
        Assert.Single(await reader.TimelineAsync(scope));

        var libraryBook = await db.UserBooks.SingleAsync();
        libraryBook.CurrentReadingUnitId = units[2].Id;
        await db.SaveChangesAsync();
        scope = await reader.ScopeAsync(userId, book.Id);
        Assert.NotNull(scope);
        detail = await reader.EntityAsync(scope, known.Id);
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Facts.Count);
        Assert.Equal(2, detail.Aliases.Count);
        Assert.Single(detail.Relations);
        Assert.NotNull(await reader.EntityAsync(scope, future.Id));
        Assert.Single(await reader.EntitiesAsync(scope, query: "Rainha oculta"));
        Assert.Equal(2, (await reader.TimelineAsync(scope)).Count);

        libraryBook.CurrentReadingUnitId = units[0].Id;
        await db.SaveChangesAsync();
        scope = await reader.ScopeAsync(userId, book.Id);
        Assert.NotNull(scope);
        detail = await reader.EntityAsync(scope, known.Id);
        Assert.NotNull(detail);
        Assert.Single(detail.Facts);
        Assert.Empty(await reader.TimelineAsync(scope));

        libraryBook.CurrentReadingUnitId = null;
        await db.SaveChangesAsync();
        scope = await reader.ScopeAsync(userId, book.Id);
        Assert.NotNull(scope);
        Assert.Empty(await reader.EntitiesAsync(scope));
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
