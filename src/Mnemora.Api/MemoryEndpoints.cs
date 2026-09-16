using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class MemoryEndpoints
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var memory = app.MapGroup("/api").RequireAuthorization().WithTags("Memory");

        memory.MapGet("/books/{bookId:guid}/notes", async (
            Guid bookId, Guid? entityId, ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            if (entityId is Guid requestedEntity
                && !await reader.KnownEntities(scope).AnyAsync(x => x.Id == requestedEntity))
                return Results.NotFound();

            var notes = VisibleNotes(db, reader, scope);
            if (entityId is Guid entity) notes = notes.Where(x => x.EntityId == entity);
            var rows = await notes.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync();
            return Results.Ok(rows.Select(NoteDto.From));
        });

        memory.MapPost("/books/{bookId:guid}/notes", async (
            Guid bookId, NoteCreate request, ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            var content = CleanContent(request.Content);
            if (content is null) return InvalidContent();
            if (!await ReferencesAreKnown(db, reader, scope,
                    request.EntityId, request.ReadingUnitId))
                return InvalidRequest("Referência de nota inválida para seu ponto de leitura.");

            var note = new UserNote
            {
                UserId = scope.UserId,
                BookId = bookId,
                EntityId = request.EntityId,
                ReadingUnitId = request.ReadingUnitId,
                Content = content
            };
            db.UserNotes.Add(note);
            await db.SaveChangesAsync();
            return Results.Created($"/api/notes/{note.Id}", NoteDto.From(note));
        });

        memory.MapPut("/notes/{noteId:guid}", async (
            Guid noteId, NoteUpdate request, ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var userId = CurrentUser.Id(user);
            var owned = await db.UserNotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == noteId && x.UserId == userId);
            if (owned is null) return Results.NotFound();
            var scope = await reader.ScopeAsync(userId, owned.BookId);
            if (scope is null) return Results.NotFound();
            var note = await VisibleNotes(db, reader, scope).AsTracking()
                .FirstOrDefaultAsync(x => x.Id == noteId);
            if (note is null) return Results.NotFound();
            var content = CleanContent(request.Content);
            if (content is null) return InvalidContent();

            note.Content = content;
            note.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(NoteDto.From(note));
        });

        memory.MapDelete("/notes/{noteId:guid}", async (
            Guid noteId, ClaimsPrincipal user, MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var userId = CurrentUser.Id(user);
            var owned = await db.UserNotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == noteId && x.UserId == userId);
            if (owned is null) return Results.NotFound();
            var scope = await reader.ScopeAsync(userId, owned.BookId);
            if (scope is null) return Results.NotFound();
            var note = await VisibleNotes(db, reader, scope).AsTracking()
                .FirstOrDefaultAsync(x => x.Id == noteId);
            if (note is null) return Results.NotFound();

            db.UserNotes.Remove(note);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        memory.MapGet("/books/{bookId:guid}/review", async (
            Guid bookId, ClaimsPrincipal user, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            return scope is null ? Results.NotFound()
                : Results.Ok(await reader.ReviewAsync(scope));
        });

        memory.MapPost("/books/{bookId:guid}/review/{entityId:guid}", async (
            Guid bookId, Guid entityId, ReviewFeedback request, ClaimsPrincipal user,
            MnemoraDbContext db, KnowledgeReader reader) =>
        {
            var scope = await reader.ScopeAsync(CurrentUser.Id(user), bookId);
            if (scope is null) return Results.NotFound();
            if (request.Remembered is not bool remembered)
                return InvalidRequest("Informe o resultado da revisão.");
            if (!await reader.KnownEntities(scope).AnyAsync(x => x.Id == entityId))
                return Results.NotFound();

            db.UserRecallActivities.Add(new UserRecallActivity
            {
                UserId = scope.UserId,
                BookId = bookId,
                EntityId = entityId,
                Remembered = remembered,
                At = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static IQueryable<UserNote> VisibleNotes(
        MnemoraDbContext db, KnowledgeReader reader, KnowledgeScope scope)
    {
        var notes = db.UserNotes.Where(x =>
            x.UserId == scope.UserId && x.BookId == scope.BookId);
        if (scope.CurrentOrder is not int order)
            return notes.Where(x => x.EntityId == null && x.ReadingUnitId == null);

        var knownEntityIds = reader.KnownEntities(scope).Select(x => x.Id);
        var knownUnitIds = db.ReadingUnits.AsNoTracking()
            .Where(x => x.BookId == scope.BookId && x.OrderIndex <= order)
            .Select(x => x.Id);
        return notes.Where(x =>
            (!x.EntityId.HasValue || knownEntityIds.Contains(x.EntityId.Value))
            && (!x.ReadingUnitId.HasValue || knownUnitIds.Contains(x.ReadingUnitId.Value)));
    }

    private static async Task<bool> ReferencesAreKnown(
        MnemoraDbContext db, KnowledgeReader reader, KnowledgeScope scope,
        Guid? entityId, Guid? readingUnitId)
    {
        if (entityId is Guid entity
            && !await reader.KnownEntities(scope).AnyAsync(x => x.Id == entity))
            return false;
        if (readingUnitId is Guid unit)
        {
            if (scope.CurrentOrder is not int order) return false;
            if (!await db.ReadingUnits.AsNoTracking().AnyAsync(x =>
                    x.Id == unit && x.BookId == scope.BookId && x.OrderIndex <= order))
                return false;
        }
        return true;
    }

    private static string? CleanContent(string? content)
    {
        var cleaned = content?.Trim();
        return string.IsNullOrEmpty(cleaned) || cleaned.Length > 4000 ? null : cleaned;
    }

    private static IResult InvalidContent() => InvalidRequest(
        "A nota deve ter entre 1 e 4.000 caracteres.");

    private static IResult InvalidRequest(string detail) => Results.Problem(
        detail, statusCode: StatusCodes.Status400BadRequest);

    private sealed record NoteCreate(
        string? Content, Guid? EntityId, Guid? ReadingUnitId);
    private sealed record NoteUpdate(string? Content);
    private sealed record ReviewFeedback(bool? Remembered);
}

public sealed record NoteDto(
    Guid Id, Guid BookId, Guid? EntityId, Guid? ReadingUnitId,
    string Content, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static NoteDto From(UserNote note) => new(
        note.Id, note.BookId, note.EntityId, note.ReadingUnitId,
        note.Content, note.CreatedAt, note.UpdatedAt);
}
