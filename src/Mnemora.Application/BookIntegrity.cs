using Mnemora.Domain;

namespace Mnemora.Application;

public static class BookIntegrity
{
    public static void ReadingUnit(ReadingUnit unit, ReadingUnit? parent)
    {
        if (unit.OrderIndex <= 0 || string.IsNullOrWhiteSpace(unit.SafeLabel))
            throw new ArgumentException("A unidade precisa de ordem positiva e rótulo seguro.");
        if (parent is not null && (parent.BookId != unit.BookId || parent.Id == unit.Id))
            throw new ArgumentException("A unidade pai deve pertencer ao mesmo livro.");
    }

    public static void Entity(LoreEntity entity, ReadingUnit firstKnown)
    {
        if (entity.BookId != firstKnown.BookId)
            throw new ArgumentException("A primeira revelação deve pertencer ao livro da entidade.");
    }

    public static void Fact(LoreEntity entity, ReadingUnit firstKnown, ReadingUnit reveal)
    {
        Entity(entity, firstKnown);
        if (reveal.BookId != entity.BookId || reveal.OrderIndex < firstKnown.OrderIndex)
            throw new ArgumentException("O fato deve ser revelado no mesmo livro após a entidade.");
    }

    public static void Relation(
        Guid bookId,
        LoreEntity source,
        ReadingUnit sourceFirst,
        LoreEntity target,
        ReadingUnit targetFirst,
        ReadingUnit reveal)
    {
        if (source.BookId != bookId || target.BookId != bookId || reveal.BookId != bookId
            || sourceFirst.BookId != bookId || targetFirst.BookId != bookId
            || reveal.OrderIndex < Math.Max(sourceFirst.OrderIndex, targetFirst.OrderIndex))
            throw new ArgumentException("A relação deve pertencer ao livro e aparecer após ambas as entidades.");
    }
}
