using Microsoft.EntityFrameworkCore;

namespace Mnemora.Infrastructure;

// Preflight for admin reordering. A change cannot make gated content predate its subject.
public sealed class BookConsistency(MnemoraDbContext db)
{
    public async Task<string?> CheckEntityFirstKnownAsync(
        Guid entityId, Guid bookId, Guid proposedFirstUnitId)
    {
        var orders = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == bookId)
            .ToDictionaryAsync(x => x.Id, x => x.OrderIndex);
        if (!orders.TryGetValue(proposedFirstUnitId, out var firstOrder))
            return "Primeira revelação inválida.";
        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => x.EntityId == entityId).ToListAsync();
        var facts = await db.LoreFacts.AsNoTracking()
            .Where(x => x.EntityId == entityId).ToListAsync();
        if (aliases.Any(x => !orders.ContainsKey(x.RevealAtUnitId)
                || orders[x.RevealAtUnitId] < firstOrder)
            || facts.Any(x => !orders.ContainsKey(x.RevealAtUnitId)
                || orders[x.RevealAtUnitId] < firstOrder))
            return "Um alias ou fato existente ficaria antes da entidade.";
        var relations = await db.LoreRelations.AsNoTracking().Where(x =>
            x.SourceEntityId == entityId || x.TargetEntityId == entityId).ToListAsync();
        var otherIds = relations.Select(x =>
            x.SourceEntityId == entityId ? x.TargetEntityId : x.SourceEntityId).Distinct().ToList();
        var others = await db.LoreEntities.AsNoTracking().Where(x => otherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FirstKnownAtUnitId);
        if (relations.Any(x =>
            !orders.ContainsKey(x.RevealAtUnitId)
            || !others.TryGetValue(
                x.SourceEntityId == entityId ? x.TargetEntityId : x.SourceEntityId, out var otherFirst)
            || !orders.ContainsKey(otherFirst)
            || orders[x.RevealAtUnitId] < Math.Max(firstOrder, orders[otherFirst])))
            return "Uma relação existente ficaria antes de seus participantes.";
        return null;
    }

    public async Task<string?> CheckOrderAsync(Guid bookId,
        IReadOnlyDictionary<Guid, int>? proposedOrders = null)
    {
        var units = await db.ReadingUnits.AsNoTracking()
            .Where(x => x.BookId == bookId).ToListAsync();
        var orders = units.ToDictionary(x => x.Id, x => x.OrderIndex);
        if (proposedOrders is not null)
            foreach (var (id, order) in proposedOrders)
            {
                if (!orders.ContainsKey(id) || order <= 0)
                    return "Ordem de unidade inválida.";
                orders[id] = order;
            }
        if (orders.Values.Distinct().Count() != orders.Count)
            return "Duas unidades não podem ter a mesma ordem.";
        if (units.Any(x => x.ParentUnitId is Guid parent
                && (!orders.ContainsKey(parent) || orders[parent] >= orders[x.Id])))
            return "A unidade pai precisa vir antes dos filhos.";

        var entities = await db.LoreEntities.AsNoTracking()
            .Where(x => x.BookId == bookId).ToListAsync();
        if (entities.Any(x => !orders.ContainsKey(x.FirstKnownAtUnitId)))
            return "Uma entidade usa unidade de outro livro.";
        var entityById = entities.ToDictionary(x => x.Id);
        var ids = entityById.Keys.ToList();
        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId)).ToListAsync();
        var facts = await db.LoreFacts.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId)).ToListAsync();
        if (aliases.Any(x => !orders.ContainsKey(x.RevealAtUnitId)
                || orders[x.RevealAtUnitId] < orders[entityById[x.EntityId].FirstKnownAtUnitId])
            || facts.Any(x => !orders.ContainsKey(x.RevealAtUnitId)
                || orders[x.RevealAtUnitId] < orders[entityById[x.EntityId].FirstKnownAtUnitId]))
            return "Um alias ou fato ficaria antes da entidade.";

        var relations = await db.LoreRelations.AsNoTracking()
            .Where(x => x.BookId == bookId).ToListAsync();
        if (relations.Any(x =>
                !orders.ContainsKey(x.RevealAtUnitId)
                || !entityById.ContainsKey(x.SourceEntityId)
                || !entityById.ContainsKey(x.TargetEntityId)
                || orders[x.RevealAtUnitId] < Math.Max(
                    orders[entityById[x.SourceEntityId].FirstKnownAtUnitId],
                    orders[entityById[x.TargetEntityId].FirstKnownAtUnitId])))
            return "Uma relação ficaria antes de seus participantes.";
        return null;
    }
}
