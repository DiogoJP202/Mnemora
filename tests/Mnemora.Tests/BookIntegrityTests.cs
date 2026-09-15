using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Tests;

public sealed class BookIntegrityTests
{
    [Fact]
    public void Reading_unit_cannot_use_parent_from_another_book()
    {
        var unit = Unit(Guid.NewGuid(), 20);
        var parent = Unit(Guid.NewGuid(), 10);

        Assert.Throws<ArgumentException>(() => BookIntegrity.ReadingUnit(unit, parent));
    }

    [Fact]
    public void Fact_cannot_reveal_before_entity_or_in_another_book()
    {
        var book = Guid.NewGuid();
        var entity = Entity(book);
        var first = Unit(book, 20);
        var earlier = Unit(book, 10);
        var otherBook = Unit(Guid.NewGuid(), 30);

        Assert.Throws<ArgumentException>(() => BookIntegrity.Fact(entity, first, earlier));
        Assert.Throws<ArgumentException>(() => BookIntegrity.Fact(entity, first, otherBook));
        BookIntegrity.Fact(entity, first, Unit(book, 20));
    }

    [Fact]
    public void Relation_must_wait_until_both_entities_are_known()
    {
        var book = Guid.NewGuid();
        var source = Entity(book);
        var target = Entity(book);
        var sourceFirst = Unit(book, 10);
        var targetFirst = Unit(book, 40);

        Assert.Throws<ArgumentException>(() => BookIntegrity.Relation(
            book, source, sourceFirst, target, targetFirst, Unit(book, 20)));
        BookIntegrity.Relation(book, source, sourceFirst, target, targetFirst, Unit(book, 40));
    }

    private static ReadingUnit Unit(Guid bookId, int order) => new()
    {
        BookId = bookId,
        Title = $"Capítulo {order}",
        SafeLabel = $"Unidade {order}",
        Slug = $"unit-{order}",
        OrderIndex = order
    };

    private static LoreEntity Entity(Guid bookId) => new()
    {
        BookId = bookId,
        Name = "Teste",
        Slug = Guid.NewGuid().ToString("N")
    };
}
