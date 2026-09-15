using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminRelationEndpoints
{
    public static IEndpointRouteBuilder MapAdminRelationEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin").WithTags("Admin Relations");

        admin.MapGet("/books/{bookId:guid}/relations", async (Guid bookId, MnemoraDbContext db) =>
            Results.Ok(await db.LoreRelations.AsNoTracking().Where(x => x.BookId == bookId)
                .OrderBy(x => x.CreatedAt).Select(x => new RelationDto(x.Id, x.BookId,
                    x.SourceEntityId, x.TargetEntityId, x.RelationType,
                    x.Label, x.RevealAtUnitId)).ToListAsync()));

        admin.MapPost("/books/{bookId:guid}/relations", async (
            Guid bookId, RelationWrite request, MnemoraDbContext db) =>
        {
            var error = await ValidateAsync(db, bookId, request);
            if (error is not null) return error;
            var relation = ToRelation(bookId, request);
            db.LoreRelations.Add(relation);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/relations/{relation.Id}", ToDto(relation));
        });

        admin.MapPut("/relations/{relationId:guid}", async (
            Guid relationId, RelationWrite request, MnemoraDbContext db) =>
        {
            var relation = await db.LoreRelations.FindAsync(relationId);
            if (relation is null) return Results.NotFound();
            var error = await ValidateAsync(db, relation.BookId, request);
            if (error is not null) return error;
            relation.SourceEntityId = request.SourceEntityId;
            relation.TargetEntityId = request.TargetEntityId;
            relation.RelationType = request.RelationType.Trim();
            relation.Label = request.Label?.Trim();
            relation.RevealAtUnitId = request.RevealAtUnitId;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(relation));
        });

        admin.MapDelete("/relations/{relationId:guid}", async (
            Guid relationId, MnemoraDbContext db) =>
        {
            var relation = await db.LoreRelations.FindAsync(relationId);
            if (relation is null) return Results.NotFound();
            db.LoreRelations.Remove(relation);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<IResult?> ValidateAsync(
        MnemoraDbContext db, Guid bookId, RelationWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.RelationType))
            return Results.BadRequest("Informe o tipo de relação.");
        var source = await db.LoreEntities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.SourceEntityId && x.BookId == bookId);
        var target = await db.LoreEntities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.TargetEntityId && x.BookId == bookId);
        var reveal = await db.ReadingUnits.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.RevealAtUnitId && x.BookId == bookId);
        if (source is null || target is null || reveal is null)
            return Results.BadRequest("Entidades ou unidade de revelação inválidas.");
        var sourceFirst = await db.ReadingUnits.AsNoTracking()
            .FirstAsync(x => x.Id == source.FirstKnownAtUnitId);
        var targetFirst = await db.ReadingUnits.AsNoTracking()
            .FirstAsync(x => x.Id == target.FirstKnownAtUnitId);
        if (reveal.OrderIndex < Math.Max(sourceFirst.OrderIndex, targetFirst.OrderIndex))
            return Results.BadRequest("A relação deve ser revelada após ambas as entidades.");
        BookIntegrity.Relation(bookId, source, sourceFirst, target, targetFirst, reveal);
        return null;
    }

    private static LoreRelation ToRelation(Guid bookId, RelationWrite request) => new()
    {
        BookId = bookId, SourceEntityId = request.SourceEntityId,
        TargetEntityId = request.TargetEntityId,
        RelationType = request.RelationType.Trim(), Label = request.Label?.Trim(),
        RevealAtUnitId = request.RevealAtUnitId
    };

    private static RelationDto ToDto(LoreRelation x) =>
        new(x.Id, x.BookId, x.SourceEntityId, x.TargetEntityId,
            x.RelationType, x.Label, x.RevealAtUnitId);

    private sealed record RelationWrite(Guid SourceEntityId, Guid TargetEntityId,
        string RelationType, string? Label, Guid RevealAtUnitId);
    private sealed record RelationDto(Guid Id, Guid BookId, Guid SourceEntityId,
        Guid TargetEntityId, string RelationType, string? Label, Guid RevealAtUnitId);
}
