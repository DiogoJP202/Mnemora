using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminBookEndpoints
{
    public static IEndpointRouteBuilder MapAdminBookEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin").WithTags("Admin Books");

        admin.MapGet("/books", async (MnemoraDbContext db) =>
            Results.Ok(await db.Books.AsNoTracking().OrderBy(x => x.Title)
                .Select(x => new BookDto(x.Id, x.Title, x.Author, x.Description, x.CoverUrl))
                .ToListAsync()));

        admin.MapGet("/books/{bookId:guid}", async (Guid bookId, MnemoraDbContext db) =>
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookId);
            return book is null ? Results.NotFound() : Results.Ok(
                new BookDto(book.Id, book.Title, book.Author, book.Description, book.CoverUrl));
        });

        admin.MapPost("/books", async (BookWrite request, MnemoraDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Author))
                return Results.BadRequest("Título e autor são obrigatórios.");
            var book = new Book
            {
                Title = request.Title.Trim(), Author = request.Author.Trim(),
                Description = request.Description?.Trim(), CoverUrl = request.CoverUrl?.Trim()
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/books/{book.Id}",
                new BookDto(book.Id, book.Title, book.Author, book.Description, book.CoverUrl));
        });

        admin.MapPut("/books/{bookId:guid}", async (
            Guid bookId, BookWrite request, MnemoraDbContext db) =>
        {
            var book = await db.Books.FindAsync(bookId);
            if (book is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Author))
                return Results.BadRequest("Título e autor são obrigatórios.");
            book.Title = request.Title.Trim();
            book.Author = request.Author.Trim();
            book.Description = request.Description?.Trim();
            book.CoverUrl = request.CoverUrl?.Trim();
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new BookDto(book.Id, book.Title, book.Author,
                book.Description, book.CoverUrl));
        });

        admin.MapDelete("/books/{bookId:guid}", async (Guid bookId, MnemoraDbContext db) =>
        {
            var book = await db.Books.FindAsync(bookId);
            if (book is null) return Results.NotFound();
            if (await db.ReadingUnits.AnyAsync(x => x.BookId == bookId)
                || await db.LoreEntities.AnyAsync(x => x.BookId == bookId)
                || await db.UserBooks.AnyAsync(x => x.BookId == bookId))
                return Results.Conflict("Remova o conteúdo e as associações antes do livro.");
            db.Books.Remove(book);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapGet("/books/{bookId:guid}/reading-units", async (
            Guid bookId, MnemoraDbContext db) =>
            Results.Ok(await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == bookId)
                .OrderBy(x => x.OrderIndex)
                .Select(x => new UnitDto(x.Id, x.BookId, x.ParentUnitId, x.Title,
                    x.SafeLabel, x.Slug, x.Type.ToString(), x.OrderIndex))
                .ToListAsync()));

        admin.MapPost("/books/{bookId:guid}/reading-units", async (
            Guid bookId, UnitWrite request, MnemoraDbContext db) =>
        {
            if (!await db.Books.AnyAsync(x => x.Id == bookId)) return Results.NotFound();
            var validation = await ValidateUnitAsync(db, bookId, null, request);
            if (validation is not null) return validation;
            var unit = ToUnit(bookId, request);
            var parent = request.ParentUnitId is Guid parentId
                ? await db.ReadingUnits.FindAsync(parentId) : null;
            BookIntegrity.ReadingUnit(unit, parent);
            if (parent is not null && parent.OrderIndex >= unit.OrderIndex)
                return Results.BadRequest("A unidade pai precisa vir antes da filha.");
            db.ReadingUnits.Add(unit);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/books/{bookId}/reading-units/{unit.Id}", ToDto(unit));
        });

        admin.MapPut("/books/{bookId:guid}/reading-units/{unitId:guid}", async (
            Guid bookId, Guid unitId, UnitWrite request, MnemoraDbContext db,
            BookConsistency consistency) =>
        {
            var unit = await db.ReadingUnits.FirstOrDefaultAsync(x => x.Id == unitId && x.BookId == bookId);
            if (unit is null) return Results.NotFound();
            var validation = await ValidateUnitAsync(db, bookId, unitId, request);
            if (validation is not null) return validation;
            if (request.ParentUnitId is Guid parentId
                && await HasAncestorAsync(db, parentId, unitId))
                return Results.Conflict("A hierarquia não pode ter ciclos.");
            var orderError = await consistency.CheckOrderAsync(bookId,
                new Dictionary<Guid, int> { [unitId] = request.OrderIndex });
            if (orderError is not null) return Results.Conflict(orderError);
            unit.ParentUnitId = request.ParentUnitId;
            unit.Title = request.Title.Trim();
            unit.SafeLabel = request.SafeLabel.Trim();
            unit.Slug = request.Slug.Trim();
            unit.OrderIndex = request.OrderIndex;
            unit.Type = Enum.Parse<ReadingUnitType>(request.Type, true);
            var parent = request.ParentUnitId is Guid id ? await db.ReadingUnits.FindAsync(id) : null;
            BookIntegrity.ReadingUnit(unit, parent);
            if (parent is not null && parent.OrderIndex >= unit.OrderIndex)
                return Results.Conflict("A unidade pai precisa vir antes da filha.");
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(unit));
        });

        admin.MapDelete("/books/{bookId:guid}/reading-units/{unitId:guid}", async (
            Guid bookId, Guid unitId, MnemoraDbContext db) =>
        {
            var unit = await db.ReadingUnits.FirstOrDefaultAsync(x => x.Id == unitId && x.BookId == bookId);
            if (unit is null) return Results.NotFound();
            if (await db.ReadingUnits.AnyAsync(x => x.ParentUnitId == unitId)
                || await db.UserBooks.AnyAsync(x => x.CurrentReadingUnitId == unitId)
                || await db.LoreEntities.AnyAsync(x => x.FirstKnownAtUnitId == unitId)
                || await db.LoreFacts.AnyAsync(x => x.RevealAtUnitId == unitId)
                || await db.LoreRelations.AnyAsync(x => x.RevealAtUnitId == unitId)
                || await db.EntityAliases.AnyAsync(x => x.RevealAtUnitId == unitId))
                return Results.Conflict("A unidade está em uso.");
            db.ReadingUnits.Remove(unit);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPost("/books/{bookId:guid}/reading-units/{unitId:guid}/move", async (
            Guid bookId, Guid unitId, string direction, MnemoraDbContext db,
            BookConsistency consistency) =>
        {
            var units = await db.ReadingUnits.Where(x => x.BookId == bookId)
                .OrderBy(x => x.OrderIndex).ToListAsync();
            var index = units.FindIndex(x => x.Id == unitId);
            if (index < 0) return Results.NotFound();
            var otherIndex = direction.ToLowerInvariant() switch
            {
                "up" => index - 1,
                "down" => index + 1,
                _ => -1
            };
            if (otherIndex < 0 || otherIndex >= units.Count)
                return Results.BadRequest("Movimento indisponível.");
            var current = units[index];
            var other = units[otherIndex];
            if (BreaksHierarchy(current, other.OrderIndex, units)
                || BreaksHierarchy(other, current.OrderIndex, units))
                return Results.Conflict("O movimento quebraria a hierarquia.");
            var orderError = await consistency.CheckOrderAsync(bookId,
                new Dictionary<Guid, int>
                {
                    [current.Id] = other.OrderIndex,
                    [other.Id] = current.OrderIndex
                });
            if (orderError is not null) return Results.Conflict(orderError);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.ReadingUnits.Where(x => x.Id == current.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.OrderIndex, -1));
            await db.ReadingUnits.Where(x => x.Id == other.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.OrderIndex, current.OrderIndex));
            await db.ReadingUnits.Where(x => x.Id == current.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.OrderIndex, other.OrderIndex));
            await transaction.CommitAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<IResult?> ValidateUnitAsync(
        MnemoraDbContext db, Guid bookId, Guid? unitId, UnitWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.SafeLabel)
            || string.IsNullOrWhiteSpace(request.Slug) || request.OrderIndex <= 0
            || !Enum.TryParse<ReadingUnitType>(request.Type, true, out _))
            return Results.BadRequest("Informe título, rótulo seguro, slug, tipo e ordem positiva.");
        if (request.ParentUnitId is Guid parentId
            && !await db.ReadingUnits.AnyAsync(x => x.Id == parentId && x.BookId == bookId))
            return Results.BadRequest("Unidade pai inválida.");
        if (await db.ReadingUnits.AnyAsync(x => x.BookId == bookId && x.Id != unitId
            && (x.OrderIndex == request.OrderIndex || x.Slug == request.Slug.Trim())))
            return Results.Conflict("Ordem ou slug já existe no livro.");
        return null;
    }

    private static async Task<bool> HasAncestorAsync(
        MnemoraDbContext db, Guid parentId, Guid childId)
    {
        var cursor = (Guid?)parentId;
        while (cursor is Guid id)
        {
            if (id == childId) return true;
            cursor = await db.ReadingUnits.Where(x => x.Id == id)
                .Select(x => x.ParentUnitId).FirstOrDefaultAsync();
        }
        return false;
    }

    private static bool BreaksHierarchy(ReadingUnit unit, int newOrder,
        IReadOnlyList<ReadingUnit> units) =>
        (unit.ParentUnitId is Guid parentId
            && units.Any(x => x.Id == parentId && x.OrderIndex >= newOrder))
        || units.Any(x => x.ParentUnitId == unit.Id && x.OrderIndex <= newOrder);

    private static ReadingUnit ToUnit(Guid bookId, UnitWrite request) => new()
    {
        BookId = bookId, ParentUnitId = request.ParentUnitId,
        Title = request.Title.Trim(), SafeLabel = request.SafeLabel.Trim(),
        Slug = request.Slug.Trim(), OrderIndex = request.OrderIndex,
        Type = Enum.Parse<ReadingUnitType>(request.Type, true)
    };

    private static UnitDto ToDto(ReadingUnit x) =>
        new(x.Id, x.BookId, x.ParentUnitId, x.Title, x.SafeLabel,
            x.Slug, x.Type.ToString(), x.OrderIndex);

    private sealed record BookWrite(string Title, string Author, string? Description, string? CoverUrl);
    private sealed record BookDto(Guid Id, string Title, string Author, string? Description, string? CoverUrl);
    private sealed record UnitWrite(Guid? ParentUnitId, string Title, string SafeLabel,
        string Slug, string Type, int OrderIndex);
    private sealed record UnitDto(Guid Id, Guid BookId, Guid? ParentUnitId,
        string Title, string SafeLabel, string Slug, string Type, int OrderIndex);
}
