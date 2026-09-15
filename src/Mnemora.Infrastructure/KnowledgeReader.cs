using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

// The only public read path for lore. Predicates run in SQL before DTO creation.
public sealed class KnowledgeReader(MnemoraDbContext db)
{
    public async Task<KnowledgeScope?> ScopeAsync(Guid userId, Guid bookId)
    {
        var libraryBook = await db.UserBooks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.BookId == bookId);
        if (libraryBook is null) return null;
        int? order = null;
        if (libraryBook.CurrentReadingUnitId is Guid unitId)
            order = await db.ReadingUnits.AsNoTracking()
                .Where(x => x.Id == unitId && x.BookId == bookId)
                .Select(x => (int?)x.OrderIndex).FirstOrDefaultAsync();
        return new KnowledgeScope(userId, bookId, order);
    }

    public IQueryable<LoreEntity> KnownEntities(KnowledgeScope scope)
    {
        if (scope.CurrentOrder is not int order)
            return db.LoreEntities.Where(_ => false);
        return from entity in db.LoreEntities.AsNoTracking()
               join unit in db.ReadingUnits.AsNoTracking()
                   on entity.FirstKnownAtUnitId equals unit.Id
               where entity.BookId == scope.BookId && unit.BookId == scope.BookId
                   && unit.OrderIndex <= order
               select entity;
    }

    public IQueryable<EntityAlias> KnownAliases(KnowledgeScope scope)
    {
        if (scope.CurrentOrder is not int order)
            return db.EntityAliases.Where(_ => false);
        return from alias in db.EntityAliases.AsNoTracking()
               join unit in db.ReadingUnits.AsNoTracking()
                   on alias.RevealAtUnitId equals unit.Id
               join entity in KnownEntities(scope) on alias.EntityId equals entity.Id
               where unit.BookId == scope.BookId && unit.OrderIndex <= order
               select alias;
    }

    public IQueryable<LoreFact> KnownFacts(KnowledgeScope scope)
    {
        if (scope.CurrentOrder is not int order)
            return db.LoreFacts.Where(_ => false);
        return from fact in db.LoreFacts.AsNoTracking()
               join unit in db.ReadingUnits.AsNoTracking()
                   on fact.RevealAtUnitId equals unit.Id
               join entity in KnownEntities(scope) on fact.EntityId equals entity.Id
               where unit.BookId == scope.BookId && unit.OrderIndex <= order
               select fact;
    }

    public IQueryable<LoreRelation> KnownRelations(KnowledgeScope scope)
    {
        if (scope.CurrentOrder is not int order)
            return db.LoreRelations.Where(_ => false);
        return from relation in db.LoreRelations.AsNoTracking()
               join unit in db.ReadingUnits.AsNoTracking()
                   on relation.RevealAtUnitId equals unit.Id
               join source in KnownEntities(scope) on relation.SourceEntityId equals source.Id
               join target in KnownEntities(scope) on relation.TargetEntityId equals target.Id
               where relation.BookId == scope.BookId && unit.BookId == scope.BookId
                   && unit.OrderIndex <= order
               select relation;
    }

    public async Task<IReadOnlyList<LoreEntitySummaryDto>> EntitiesAsync(
        KnowledgeScope scope, LoreEntityType? type = null, string? query = null)
    {
        var entities = KnownEntities(scope);
        if (type is not null) entities = entities.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            entities = entities.Where(entity =>
                entity.Name.ToLower().Contains(term)
                || (entity.ShortDescription != null
                    && entity.ShortDescription.ToLower().Contains(term))
                || KnownAliases(scope).Any(alias =>
                    alias.EntityId == entity.Id && alias.Alias.ToLower().Contains(term))
                || KnownFacts(scope).Any(fact =>
                    fact.EntityId == entity.Id
                    && (fact.Content.ToLower().Contains(term)
                        || (fact.MemoryHint != null && fact.MemoryHint.ToLower().Contains(term)))));
        }
        var rows = await entities.OrderByDescending(x => x.Importance)
            .ThenBy(x => x.Name).Take(100).ToListAsync();
        return await SummariesAsync(scope, rows);
    }

    public async Task<LoreEntityDetailDto?> EntityAsync(KnowledgeScope scope, Guid entityId)
    {
        var entity = await KnownEntities(scope).FirstOrDefaultAsync(x => x.Id == entityId);
        if (entity is null) return null;
        var aliases = await KnownAliases(scope).Where(x => x.EntityId == entityId)
            .OrderBy(x => x.Alias).Select(x => x.Alias).ToListAsync();
        var facts = await KnownFacts(scope).Where(x => x.EntityId == entityId)
            .OrderByDescending(x => x.Importance).ThenBy(x => x.CreatedAt)
            .Select(x => new LoreFactDto(x.Id, x.Content, x.MemoryHint, x.Type.ToString()))
            .ToListAsync();
        var relations = await KnownRelations(scope)
            .Where(x => x.SourceEntityId == entityId || x.TargetEntityId == entityId)
            .ToListAsync();
        var relatedIds = relations.SelectMany(x => new[] { x.SourceEntityId, x.TargetEntityId })
            .Distinct().ToList();
        var names = await KnownEntities(scope).Where(x => relatedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);
        var relationDtos = relations.Where(x =>
                names.ContainsKey(x.SourceEntityId) && names.ContainsKey(x.TargetEntityId))
            .Select(x => new LoreRelationDto(x.Id, x.SourceEntityId, names[x.SourceEntityId],
                x.TargetEntityId, names[x.TargetEntityId], x.RelationType, x.Label)).ToList();
        var summary = (await SummariesAsync(scope, [entity]))[0];
        return new LoreEntityDetailDto(summary, aliases, facts, relationDtos);
    }

    public async Task<IReadOnlyList<TimelineEventDto>> TimelineAsync(KnowledgeScope scope)
    {
        var events = await KnownEntities(scope).Where(x => x.Type == LoreEntityType.Event)
            .OrderBy(x => x.ChronologyIndex).ThenBy(x => x.Name).Take(100).ToListAsync();
        var summaries = await SummariesAsync(scope, events);
        return events.Select((x, index) => new TimelineEventDto(summaries[index], x.ChronologyIndex))
            .ToList();
    }

    private async Task<IReadOnlyList<LoreEntitySummaryDto>> SummariesAsync(
        KnowledgeScope scope, IReadOnlyList<LoreEntity> entities)
    {
        var ids = entities.Select(x => x.Id).ToList();
        var hints = await KnownFacts(scope).Where(x => ids.Contains(x.EntityId)
                && x.MemoryHint != null)
            .OrderByDescending(x => x.Importance)
            .Select(x => new { x.EntityId, x.MemoryHint }).ToListAsync();
        var hintByEntity = hints.GroupBy(x => x.EntityId)
            .ToDictionary(group => group.Key, group => group.First().MemoryHint);
        return entities.Select(x => new LoreEntitySummaryDto(x.Id, x.BookId, x.Type.ToString(),
            x.Name, x.ShortDescription, x.ImageUrl,
            hintByEntity.GetValueOrDefault(x.Id))).ToList();
    }
}
