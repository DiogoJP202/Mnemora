using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminLoreEndpoints
{
    public static IEndpointRouteBuilder MapAdminLoreEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin").WithTags("Admin Lore");

        admin.MapGet("/books/{bookId:guid}/entities", async (Guid bookId, MnemoraDbContext db) =>
            Results.Ok(await db.LoreEntities.AsNoTracking().Where(x => x.BookId == bookId)
                .OrderBy(x => x.Name).Select(x => new EntityDto(x.Id, x.BookId,
                    x.Type.ToString(), x.Name, x.Slug, x.ShortDescription, x.ImageUrl,
                    x.FirstKnownAtUnitId, x.Importance, x.ChronologyIndex)).ToListAsync()));

        admin.MapGet("/entities/{entityId:guid}", async (Guid entityId, MnemoraDbContext db) =>
        {
            var entity = await db.LoreEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entityId);
            if (entity is null) return Results.NotFound();
            var aliases = await db.EntityAliases.AsNoTracking().Where(x => x.EntityId == entityId)
                .Select(x => new AliasDto(x.Id, x.Alias, x.RevealAtUnitId)).ToListAsync();
            var facts = await db.LoreFacts.AsNoTracking().Where(x => x.EntityId == entityId)
                .Select(x => new FactDto(x.Id, x.Content, x.MemoryHint, x.Type.ToString(),
                    x.Importance, x.RevealAtUnitId)).ToListAsync();
            var relations = await db.LoreRelations.AsNoTracking().Where(x =>
                    x.SourceEntityId == entityId || x.TargetEntityId == entityId)
                .Select(x => new RelationDto(x.Id, x.BookId, x.SourceEntityId,
                    x.TargetEntityId, x.RelationType, x.Label, x.RevealAtUnitId)).ToListAsync();
            return Results.Ok(new
            {
                entity = ToDto(entity), aliases, facts, relations
            });
        });

        admin.MapPost("/books/{bookId:guid}/entities", async (
            Guid bookId, EntityWrite request, MnemoraDbContext db) =>
        {
            var error = await ValidateEntityAsync(db, bookId, null, request);
            if (error is not null) return error;
            var entity = ToEntity(bookId, request);
            var unit = await db.ReadingUnits.FindAsync(request.FirstKnownAtUnitId);
            BookIntegrity.Entity(entity, unit!);
            db.LoreEntities.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/entities/{entity.Id}", ToDto(entity));
        });

        admin.MapPut("/entities/{entityId:guid}", async (
            Guid entityId, EntityWrite request, MnemoraDbContext db,
            BookConsistency consistency) =>
        {
            var entity = await db.LoreEntities.FindAsync(entityId);
            if (entity is null) return Results.NotFound();
            var error = await ValidateEntityAsync(db, entity.BookId, entityId, request);
            if (error is not null) return error;
            var consistencyError = await consistency.CheckEntityFirstKnownAsync(
                entityId, entity.BookId, request.FirstKnownAtUnitId);
            if (consistencyError is not null) return Results.Conflict(consistencyError);
            var unit = await db.ReadingUnits.FindAsync(request.FirstKnownAtUnitId);
            BookIntegrity.Entity(entity, unit!);
            entity.Type = Enum.Parse<LoreEntityType>(request.Type, true);
            entity.Name = request.Name.Trim();
            entity.Slug = request.Slug.Trim();
            entity.ShortDescription = request.ShortDescription?.Trim();
            entity.ImageUrl = request.ImageUrl?.Trim();
            entity.FirstKnownAtUnitId = request.FirstKnownAtUnitId;
            entity.Importance = request.Importance;
            entity.ChronologyIndex = request.ChronologyIndex;
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(entity));
        });

        admin.MapDelete("/entities/{entityId:guid}", async (Guid entityId, MnemoraDbContext db) =>
        {
            var entity = await db.LoreEntities.FindAsync(entityId);
            if (entity is null) return Results.NotFound();
            if (await db.LoreRelations.AnyAsync(x =>
                    x.SourceEntityId == entityId || x.TargetEntityId == entityId)
                || await db.UserNotes.AnyAsync(x => x.EntityId == entityId)
                || await db.UserRecallActivities.AnyAsync(x => x.EntityId == entityId))
                return Results.Conflict("A entidade está em uso.");
            db.LoreEntities.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPost("/entities/{entityId:guid}/aliases", async (
            Guid entityId, AliasWrite request, MnemoraDbContext db) =>
        {
            var entity = await db.LoreEntities.FindAsync(entityId);
            if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Alias)) return Results.BadRequest("Informe o alias.");
            var reveal = await RevealUnitAsync(db, entity, request.RevealAtUnitId);
            if (reveal is null) return Results.BadRequest("Ponto de revelação inválido.");
            var first = await db.ReadingUnits.FindAsync(entity.FirstKnownAtUnitId);
            if (reveal.OrderIndex < first!.OrderIndex)
                return Results.BadRequest("O alias não pode surgir antes da entidade.");
            BookIntegrity.Fact(entity, first!, reveal);
            var alias = new EntityAlias
            {
                EntityId = entityId, Alias = request.Alias.Trim(),
                RevealAtUnitId = request.RevealAtUnitId
            };
            db.EntityAliases.Add(alias);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/aliases/{alias.Id}", ToDto(alias));
        });

        admin.MapPut("/aliases/{aliasId:guid}", async (
            Guid aliasId, AliasWrite request, MnemoraDbContext db) =>
        {
            var alias = await db.EntityAliases.FindAsync(aliasId);
            if (alias is null) return Results.NotFound();
            var entity = await db.LoreEntities.FindAsync(alias.EntityId);
            if (string.IsNullOrWhiteSpace(request.Alias)) return Results.BadRequest("Informe o alias.");
            var reveal = await RevealUnitAsync(db, entity!, request.RevealAtUnitId);
            if (reveal is null) return Results.BadRequest("Ponto de revelação inválido.");
            var first = await db.ReadingUnits.FindAsync(entity!.FirstKnownAtUnitId);
            if (reveal.OrderIndex < first!.OrderIndex)
                return Results.BadRequest("O alias não pode surgir antes da entidade.");
            BookIntegrity.Fact(entity, first!, reveal);
            alias.Alias = request.Alias.Trim();
            alias.RevealAtUnitId = request.RevealAtUnitId;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(alias));
        });

        admin.MapDelete("/aliases/{aliasId:guid}", async (Guid aliasId, MnemoraDbContext db) =>
        {
            var alias = await db.EntityAliases.FindAsync(aliasId);
            if (alias is null) return Results.NotFound();
            db.EntityAliases.Remove(alias);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPost("/entities/{entityId:guid}/facts", async (
            Guid entityId, FactWrite request, MnemoraDbContext db) =>
        {
            var entity = await db.LoreEntities.FindAsync(entityId);
            if (entity is null) return Results.NotFound();
            var error = await ValidateFactAsync(db, entity, request);
            if (error is not null) return error;
            var fact = ToFact(entityId, request);
            db.LoreFacts.Add(fact);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/facts/{fact.Id}", ToDto(fact));
        });

        admin.MapPut("/facts/{factId:guid}", async (
            Guid factId, FactWrite request, MnemoraDbContext db) =>
        {
            var fact = await db.LoreFacts.FindAsync(factId);
            if (fact is null) return Results.NotFound();
            var entity = await db.LoreEntities.FindAsync(fact.EntityId);
            var error = await ValidateFactAsync(db, entity!, request);
            if (error is not null) return error;
            fact.Content = request.Content.Trim();
            fact.MemoryHint = request.MemoryHint?.Trim();
            fact.Type = Enum.Parse<LoreFactType>(request.Type, true);
            fact.Importance = request.Importance;
            fact.RevealAtUnitId = request.RevealAtUnitId;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(fact));
        });

        admin.MapDelete("/facts/{factId:guid}", async (Guid factId, MnemoraDbContext db) =>
        {
            var fact = await db.LoreFacts.FindAsync(factId);
            if (fact is null) return Results.NotFound();
            db.LoreFacts.Remove(fact);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapGet("/books/{bookId:guid}/preview", async (
            Guid bookId, Guid atUnitId, MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var unit = await db.ReadingUnits.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == atUnitId && x.BookId == bookId);
            if (unit is null) return Results.BadRequest("Unidade inválida.");
            var scope = new KnowledgeScope(Guid.Empty, bookId, unit.OrderIndex);
            return Results.Ok(new
            {
                entities = await reader.EntitiesAsync(scope),
                timeline = await reader.TimelineAsync(scope)
            });
        });

        admin.MapGet("/entities/{entityId:guid}/preview", async (
            Guid entityId, Guid atUnitId, MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var entity = await db.LoreEntities.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == entityId);
            if (entity is null) return Results.NotFound();
            var unit = await db.ReadingUnits.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == atUnitId && x.BookId == entity.BookId);
            if (unit is null) return Results.BadRequest("Unidade inválida.");
            var detail = await reader.EntityAsync(
                new KnowledgeScope(Guid.Empty, entity.BookId, unit.OrderIndex), entityId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        return app;
    }

    private static async Task<IResult?> ValidateEntityAsync(
        MnemoraDbContext db, Guid bookId, Guid? entityId, EntityWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug)
            || !Enum.TryParse<LoreEntityType>(request.Type, true, out _)
            || request.Importance < 0)
            return Results.BadRequest("Informe nome, slug, tipo e importância válida.");
        if (!await db.ReadingUnits.AnyAsync(x =>
                x.Id == request.FirstKnownAtUnitId && x.BookId == bookId))
            return Results.BadRequest("Primeira revelação inválida.");
        if (await db.LoreEntities.AnyAsync(x => x.BookId == bookId
                && x.Id != entityId && x.Slug == request.Slug.Trim()))
            return Results.Conflict("Slug já existe no livro.");
        return null;
    }

    private static async Task<ReadingUnit?> RevealUnitAsync(
        MnemoraDbContext db, LoreEntity entity, Guid revealId) =>
        await db.ReadingUnits.FirstOrDefaultAsync(x =>
            x.Id == revealId && x.BookId == entity.BookId);

    private static async Task<IResult?> ValidateFactAsync(
        MnemoraDbContext db, LoreEntity entity, FactWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Content)
            || !Enum.TryParse<LoreFactType>(request.Type, true, out _)
            || request.Importance < 0)
            return Results.BadRequest("Informe conteúdo, tipo e importância válida.");
        var reveal = await RevealUnitAsync(db, entity, request.RevealAtUnitId);
        if (reveal is null) return Results.BadRequest("Ponto de revelação inválido.");
        var first = await db.ReadingUnits.FindAsync(entity.FirstKnownAtUnitId);
        if (reveal.OrderIndex < first!.OrderIndex)
            return Results.BadRequest("O fato não pode surgir antes da entidade.");
        BookIntegrity.Fact(entity, first, reveal);
        return null;
    }

    private static LoreEntity ToEntity(Guid bookId, EntityWrite request) => new()
    {
        BookId = bookId, Type = Enum.Parse<LoreEntityType>(request.Type, true),
        Name = request.Name.Trim(), Slug = request.Slug.Trim(),
        ShortDescription = request.ShortDescription?.Trim(),
        ImageUrl = request.ImageUrl?.Trim(), FirstKnownAtUnitId = request.FirstKnownAtUnitId,
        Importance = request.Importance, ChronologyIndex = request.ChronologyIndex
    };

    private static LoreFact ToFact(Guid entityId, FactWrite request) => new()
    {
        EntityId = entityId, Content = request.Content.Trim(),
        MemoryHint = request.MemoryHint?.Trim(),
        Type = Enum.Parse<LoreFactType>(request.Type, true),
        Importance = request.Importance, RevealAtUnitId = request.RevealAtUnitId
    };

    private static EntityDto ToDto(LoreEntity x) =>
        new(x.Id, x.BookId, x.Type.ToString(), x.Name, x.Slug,
            x.ShortDescription, x.ImageUrl, x.FirstKnownAtUnitId,
            x.Importance, x.ChronologyIndex);
    private static AliasDto ToDto(EntityAlias x) => new(x.Id, x.Alias, x.RevealAtUnitId);
    private static FactDto ToDto(LoreFact x) => new(x.Id, x.Content, x.MemoryHint,
        x.Type.ToString(), x.Importance, x.RevealAtUnitId);

    private sealed record EntityWrite(string Type, string Name, string Slug,
        string? ShortDescription, string? ImageUrl, Guid FirstKnownAtUnitId,
        int Importance, int? ChronologyIndex);
    private sealed record AliasWrite(string Alias, Guid RevealAtUnitId);
    private sealed record FactWrite(string Content, string? MemoryHint, string Type,
        int Importance, Guid RevealAtUnitId);
    private sealed record EntityDto(Guid Id, Guid BookId, string Type, string Name,
        string Slug, string? ShortDescription, string? ImageUrl,
        Guid FirstKnownAtUnitId, int Importance, int? ChronologyIndex);
    private sealed record AliasDto(Guid Id, string Alias, Guid RevealAtUnitId);
    private sealed record FactDto(Guid Id, string Content, string? MemoryHint,
        string Type, int Importance, Guid RevealAtUnitId);
    private sealed record RelationDto(Guid Id, Guid BookId, Guid SourceEntityId,
        Guid TargetEntityId, string RelationType, string? Label, Guid RevealAtUnitId);
}
