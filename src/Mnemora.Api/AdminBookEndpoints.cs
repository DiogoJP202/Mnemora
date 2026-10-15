using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Api;

public static class AdminBookEndpoints
{
    public static IEndpointRouteBuilder MapAdminBookEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization("Admin").WithTags("Admin Books");

        admin.MapGet("/books/search", (string? q, MnemoraDbContext db,
            IBookMetadataProvider provider, IMemoryCache cache, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
            BooksEndpoints.SearchAsync(q, null, db, provider, cache, loggerFactory, cancellationToken))
            .RequireRateLimiting("catalog-external");

        admin.MapPost("/books/external", async (ExternalBookImportRequest request,
            ExternalBookImporter importer, MnemoraDbContext db, CancellationToken cancellationToken) =>
        {
            try
            {
                var book = await importer.ImportAsync(request.ExternalProvider, request.ExternalId, cancellationToken);
                return Results.Ok(await ToAdminDtoAsync(db, book));
            }
            catch (BookImportException exception)
            {
                return exception.StatusCode == 404 ? Results.NotFound()
                    : Results.Problem(detail: exception.Message, statusCode: exception.StatusCode);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return Results.Problem("Não foi possível importar o livro agora.", statusCode: 503);
            }
        }).RequireRateLimiting("import-external");

        admin.MapGet("/books", async (MnemoraDbContext db) =>
        {
            var books = await db.Books.AsNoTracking()
                .Where(x => x.CatalogKind != BookCatalogKind.Private)
                .OrderByDescending(x => db.MemoryPackRequests.Count(request =>
                    request.BookId == x.Id))
                .ThenBy(x => x.Title)
                .ToListAsync();
            var result = new List<AdminBookDto>(books.Count);
            foreach (var book in books)
                result.Add(await ToAdminDtoAsync(db, book));
            return Results.Ok(result);
        });

        admin.MapGet("/books/{bookId:guid}", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId, asTracking: false);
            return book is null
                ? Results.NotFound()
                : Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapPost("/books", async (BookWrite request, MnemoraDbContext db) =>
        {
            if (!ValidBookWrite(request))
                return Results.BadRequest("Título e autor são obrigatórios.");
            var book = new Book
            {
                Title = request.Title.Trim(),
                Author = request.Author.Trim(),
                Description = request.Description?.Trim(),
                CoverUrl = request.CoverUrl?.Trim(),
                CatalogKind = BookCatalogKind.Curated,
                ReadingOutlineStatus = EditorialContentStatus.Draft,
                LoreStatus = EditorialContentStatus.Draft
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/books/{book.Id}",
                await ToAdminDtoAsync(db, book));
        });

        admin.MapPut("/books/{bookId:guid}", async (
            Guid bookId, BookWrite request, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            if (!ValidBookWrite(request))
                return Results.BadRequest("Título e autor são obrigatórios.");
            book.Title = request.Title.Trim();
            book.Author = request.Author.Trim();
            book.Description = request.Description?.Trim();
            book.CoverUrl = request.CoverUrl?.Trim();
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapDelete("/books/{bookId:guid}", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            if (await db.ReadingUnits.AnyAsync(x => x.BookId == bookId)
                || await db.LoreEntities.AnyAsync(x => x.BookId == bookId)
                || await db.UserBooks.AnyAsync(x => x.BookId == bookId))
                return Results.Conflict(
                    "Remova o conteúdo e as associações antes do livro.");
            db.Books.Remove(book);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPost("/books/{bookId:guid}/memory-pack/prepare", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            if (book.ReadingOutlineStatus == EditorialContentStatus.None)
                book.ReadingOutlineStatus = EditorialContentStatus.Draft;
            if (book.LoreStatus == EditorialContentStatus.None)
                book.LoreStatus = EditorialContentStatus.Draft;
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapPost("/books/{bookId:guid}/memory-pack/publish-outline", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            if (book.ReadingOutlineStatus != EditorialContentStatus.Draft)
                return Results.Conflict("O índice precisa estar em rascunho para ser publicado.");
            var problems = await ValidateOutlineAsync(db, bookId);
            if (problems.Count > 0) return InvalidPack(problems);
            book.ReadingOutlineStatus = EditorialContentStatus.Published;
            book.ReadingOutlinePublishedAt = DateTime.UtcNow;
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapPost("/books/{bookId:guid}/memory-pack/publish-lore", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            if (book.ReadingOutlineStatus != EditorialContentStatus.Published)
                return Results.Conflict("Publique as unidades de leitura antes do conhecimento.");
            if (book.LoreStatus != EditorialContentStatus.Draft)
                return Results.Conflict("O conhecimento precisa estar em rascunho para ser publicado.");
            var problems = await ValidateLoreAsync(db, bookId);
            if (problems.Count > 0) return InvalidPack(problems);
            book.LoreStatus = EditorialContentStatus.Published;
            book.LorePublishedAt = DateTime.UtcNow;
            book.UpdatedAt = DateTime.UtcNow;
            db.MemoryPackRequests.RemoveRange(await db.MemoryPackRequests
                .Where(x => x.BookId == bookId).ToListAsync());
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapPost("/books/{bookId:guid}/memory-pack/unpublish-lore", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            book.LoreStatus = EditorialContentStatus.Draft;
            book.LorePublishedAt = null;
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapPost("/books/{bookId:guid}/memory-pack/unpublish-outline", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            var book = await AdminBookAsync(db, bookId);
            if (book is null) return Results.NotFound();
            book.ReadingOutlineStatus = EditorialContentStatus.Draft;
            book.ReadingOutlinePublishedAt = null;
            if (book.LoreStatus == EditorialContentStatus.Published)
            {
                book.LoreStatus = EditorialContentStatus.Draft;
                book.LorePublishedAt = null;
            }
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await ToAdminDtoAsync(db, book));
        });

        admin.MapGet("/books/{bookId:guid}/reading-units", async (
            Guid bookId, MnemoraDbContext db) =>
        {
            if (await AdminBookAsync(db, bookId, asTracking: false) is null)
                return Results.NotFound();
            return Results.Ok(await db.ReadingUnits.AsNoTracking()
                .Where(x => x.BookId == bookId)
                .OrderBy(x => x.OrderIndex)
                .Select(x => new UnitDto(x.Id, x.BookId, x.ParentUnitId, x.Title,
                    x.SafeLabel, x.Slug, x.Type.ToString(), x.OrderIndex))
                .ToListAsync());
        });

        admin.MapGet("/books/{bookId:guid}/reading-outline/source", async (
            Guid bookId, MnemoraDbContext db, IReadingOutlineProvider provider,
            CancellationToken cancellationToken) =>
        {
            var book = await AdminBookAsync(db, bookId, asTracking: false);
            if (book is null) return Results.NotFound();
            if (!string.Equals(book.ExternalProvider, BookMetadataProviders.OpenLibrary,
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(book.ExternalId))
                return Results.NotFound(
                    "Este livro não está ligado a uma edição exata da Open Library.");
            try
            {
                var outline = await provider.GetAsync(
                    book.ExternalProvider!, book.ExternalId, cancellationToken);
                return outline is null
                    ? Results.NotFound(
                        "A Open Library não oferece um sumário para esta edição.")
                    : Results.Ok(outline);
            }
            catch (ReadingOutlineLimitExceededException exception)
            {
                return Results.Problem(exception.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or System.Text.Json.JsonException
                    or TaskCanceledException)
            {
                return Results.Problem(
                    "Não foi possível consultar o sumário da Open Library agora.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("catalog-external");

        admin.MapPost("/books/{bookId:guid}/reading-outline/import", async (
            Guid bookId, OutlineImport request, MnemoraDbContext db,
            CancellationToken cancellationToken) =>
        {
            var book = await EditableOutlineBookAsync(db, bookId);
            if (book is null)
                return await DraftOrNotFoundAsync(db, bookId,
                    "Coloque o índice em preparação antes de importar unidades.");
            if (request.Items is not { Count: > 0 and <= 200 }
                || request.Items.Any(x => string.IsNullOrWhiteSpace(x.Title)
                    || x.Title.Trim().Length > 300 || x.Level is < 1 or > 4))
                return Results.BadRequest("O sumário precisa ter entre 1 e 200 itens válidos.");

            var existing = await db.ReadingUnits.AsNoTracking()
                .Where(x => x.BookId == bookId).OrderBy(x => x.OrderIndex).ToListAsync(
                    cancellationToken);
            var titles = request.Items.Select(x => x.Title.Trim()).ToArray();
            if (existing.Count > 0)
            {
                if (existing.Select(x => x.Title).SequenceEqual(
                        titles, StringComparer.Ordinal))
                    return Results.Ok(existing.Select(ToDto));
                return Results.Conflict(
                    "Este livro já possui unidades. Revise ou remova o rascunho antes de importar outro sumário.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken);
            var units = new List<ReadingUnit>(titles.Length);
            var latestAtLevel = new Dictionary<int, ReadingUnit>();
            for (var index = 0; index < titles.Length; index++)
            {
                var item = request.Items[index];
                ReadingUnit? parent = null;
                for (var level = item.Level - 1; level >= 1; level--)
                    if (latestAtLevel.TryGetValue(level, out parent)) break;
                var unit = new ReadingUnit
                {
                    BookId = bookId,
                    ParentUnitId = parent?.Id,
                    Title = titles[index],
                    SafeLabel = $"Unidade {index + 1}",
                    Slug = Slug(titles[index], index + 1),
                    Type = item.Level switch
                    {
                        1 => ReadingUnitType.Chapter,
                        2 => ReadingUnitType.Section,
                        _ => ReadingUnitType.Subsection
                    },
                    OrderIndex = (index + 1) * 10
                };
                units.Add(unit);
                latestAtLevel[item.Level] = unit;
                foreach (var nested in latestAtLevel.Keys
                    .Where(level => level > item.Level).ToArray())
                    latestAtLevel.Remove(nested);
            }
            db.ReadingUnits.AddRange(units);
            book.ReadingOutlineSourceUrl = request.SourceUrl?.Trim();
            book.ReadingOutlineAttribution = request.Attribution?.Trim();
            book.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created(
                $"/api/admin/books/{bookId}/reading-units",
                units.Select(ToDto));
        });

        admin.MapPost("/books/{bookId:guid}/reading-units", async (
            Guid bookId, UnitWrite request, MnemoraDbContext db) =>
        {
            if (await EditableOutlineBookAsync(db, bookId) is null)
                return await DraftOrNotFoundAsync(db, bookId,
                    "Coloque o índice em preparação antes de editar unidades.");
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
            return Results.Created(
                $"/api/admin/books/{bookId}/reading-units/{unit.Id}", ToDto(unit));
        });

        admin.MapPut("/books/{bookId:guid}/reading-units/{unitId:guid}", async (
            Guid bookId, Guid unitId, UnitWrite request, MnemoraDbContext db,
            BookConsistency consistency) =>
        {
            if (await EditableOutlineBookAsync(db, bookId) is null)
                return await DraftOrNotFoundAsync(db, bookId,
                    "Retire o índice de publicação antes de editar unidades.");
            var unit = await db.ReadingUnits.FirstOrDefaultAsync(x =>
                x.Id == unitId && x.BookId == bookId);
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
            var parent = request.ParentUnitId is Guid id
                ? await db.ReadingUnits.FindAsync(id) : null;
            BookIntegrity.ReadingUnit(unit, parent);
            if (parent is not null && parent.OrderIndex >= unit.OrderIndex)
                return Results.Conflict("A unidade pai precisa vir antes da filha.");
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(unit));
        });

        admin.MapDelete("/books/{bookId:guid}/reading-units/{unitId:guid}", async (
            Guid bookId, Guid unitId, MnemoraDbContext db) =>
        {
            if (await EditableOutlineBookAsync(db, bookId) is null)
                return await DraftOrNotFoundAsync(db, bookId,
                    "Retire o índice de publicação antes de excluir unidades.");
            var unit = await db.ReadingUnits.FirstOrDefaultAsync(x =>
                x.Id == unitId && x.BookId == bookId);
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
            if (await EditableOutlineBookAsync(db, bookId) is null)
                return await DraftOrNotFoundAsync(db, bookId,
                    "Retire o índice de publicação antes de reordenar unidades.");
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
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(x => x.OrderIndex, current.OrderIndex));
            await db.ReadingUnits.Where(x => x.Id == current.Id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(x => x.OrderIndex, other.OrderIndex));
            await transaction.CommitAsync();
            return Results.NoContent();
        });

        return app;
    }

    internal static Task<Book?> AdminBookAsync(
        MnemoraDbContext db, Guid bookId, bool asTracking = true)
    {
        var query = asTracking ? db.Books : db.Books.AsNoTracking();
        return query.FirstOrDefaultAsync(x =>
            x.Id == bookId && x.CatalogKind != BookCatalogKind.Private);
    }

    internal static Task<Book?> EditableLoreBookAsync(
        MnemoraDbContext db, Guid bookId) =>
        db.Books.FirstOrDefaultAsync(x => x.Id == bookId
            && x.CatalogKind != BookCatalogKind.Private
            && x.LoreStatus == EditorialContentStatus.Draft);

    private static Task<Book?> EditableOutlineBookAsync(
        MnemoraDbContext db, Guid bookId) =>
        db.Books.FirstOrDefaultAsync(x => x.Id == bookId
            && x.CatalogKind != BookCatalogKind.Private
            && x.ReadingOutlineStatus == EditorialContentStatus.Draft);

    internal static async Task<IResult> LoreDraftOrNotFoundAsync(
        MnemoraDbContext db, Guid bookId) =>
        await AdminBookAsync(db, bookId, asTracking: false) is null
            ? Results.NotFound()
            : Results.Conflict(
                "Coloque o conhecimento em preparação antes de editar o conteúdo.");

    private static async Task<IResult> DraftOrNotFoundAsync(
        MnemoraDbContext db, Guid bookId, string message) =>
        await AdminBookAsync(db, bookId, asTracking: false) is null
            ? Results.NotFound()
            : Results.Conflict(message);

    private static bool ValidBookWrite(BookWrite request) =>
        !string.IsNullOrWhiteSpace(request.Title)
        && !string.IsNullOrWhiteSpace(request.Author);

    private static async Task<AdminBookDto> ToAdminDtoAsync(
        MnemoraDbContext db, Book book)
    {
        var unitCount = await db.ReadingUnits.AsNoTracking()
            .CountAsync(x => x.BookId == book.Id);
        var entityQuery = db.LoreEntities.AsNoTracking()
            .Where(x => x.BookId == book.Id);
        var entityIds = entityQuery.Select(x => x.Id);
        return new AdminBookDto(
            book.Id, book.Title, book.Author, book.Description, book.CoverUrl,
            book.CatalogKind.ToString(), book.ExternalProvider, book.ExternalId,
            book.Isbn10, book.Isbn13, book.Publisher, book.PublishedDate,
            book.Language, book.ReadingOutlineSourceUrl,
            book.ReadingOutlineAttribution, book.ReadingOutlineStatus.ToString(),
            book.LoreStatus.ToString(), book.ReadingOutlinePublishedAt,
            book.LorePublishedAt,
            await db.MemoryPackRequests.AsNoTracking()
                .CountAsync(x => x.BookId == book.Id),
            unitCount,
            await entityQuery.CountAsync(),
            await db.LoreFacts.AsNoTracking()
                .CountAsync(x => entityIds.Contains(x.EntityId)),
            await db.LoreRelations.AsNoTracking()
                .CountAsync(x => x.BookId == book.Id),
            await entityQuery.CountAsync(x => x.Type == LoreEntityType.Event));
    }

    private static async Task<List<string>> ValidateOutlineAsync(
        MnemoraDbContext db, Guid bookId)
    {
        var units = await db.ReadingUnits.AsNoTracking()
            .Where(x => x.BookId == bookId).OrderBy(x => x.OrderIndex).ToListAsync();
        var problems = new List<string>();
        if (units.Count == 0)
            problems.Add("Cadastre pelo menos uma unidade de leitura.");
        if (units.Any(x => string.IsNullOrWhiteSpace(x.Title)
            || string.IsNullOrWhiteSpace(x.SafeLabel)
            || string.IsNullOrWhiteSpace(x.Slug)))
            problems.Add("Todas as unidades precisam de título, rótulo seguro e slug.");
        if (units.Select(x => x.OrderIndex).Distinct().Count() != units.Count)
            problems.Add("A ordem das unidades precisa ser única.");
        if (units.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != units.Count)
            problems.Add("Os slugs das unidades precisam ser únicos.");
        var byId = units.ToDictionary(x => x.Id);
        foreach (var unit in units.Where(x => x.ParentUnitId.HasValue))
        {
            if (!byId.TryGetValue(unit.ParentUnitId!.Value, out var parent))
                problems.Add($"A unidade {unit.SafeLabel} aponta para um pai inválido.");
            else if (parent.OrderIndex >= unit.OrderIndex)
                problems.Add($"A unidade pai de {unit.SafeLabel} precisa vir antes dela.");
        }
        return problems.Distinct().ToList();
    }

    private static async Task<List<string>> ValidateLoreAsync(
        MnemoraDbContext db, Guid bookId)
    {
        var problems = await ValidateOutlineAsync(db, bookId);
        var units = await db.ReadingUnits.AsNoTracking()
            .Where(x => x.BookId == bookId).ToDictionaryAsync(x => x.Id);
        var entities = await db.LoreEntities.AsNoTracking()
            .Where(x => x.BookId == bookId).ToListAsync();
        if (entities.Count == 0)
        {
            problems.Add("Cadastre pelo menos uma entidade de conhecimento.");
            return problems;
        }
        var byEntity = entities.ToDictionary(x => x.Id);
        foreach (var entity in entities)
            if (!units.ContainsKey(entity.FirstKnownAtUnitId))
                problems.Add($"A entidade {entity.Name} tem revelação inicial inválida.");

        var entityIds = byEntity.Keys.ToArray();
        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => entityIds.Contains(x.EntityId)).ToListAsync();
        foreach (var alias in aliases)
            ValidateReveal(problems, units, byEntity[alias.EntityId],
                alias.RevealAtUnitId, $"O alias {alias.Alias}");
        var facts = await db.LoreFacts.AsNoTracking()
            .Where(x => entityIds.Contains(x.EntityId)).ToListAsync();
        foreach (var fact in facts)
            ValidateReveal(problems, units, byEntity[fact.EntityId],
                fact.RevealAtUnitId, "Um fato");
        var relations = await db.LoreRelations.AsNoTracking()
            .Where(x => x.BookId == bookId).ToListAsync();
        foreach (var relation in relations)
        {
            if (!byEntity.TryGetValue(relation.SourceEntityId, out var source)
                || !byEntity.TryGetValue(relation.TargetEntityId, out var target)
                || !units.TryGetValue(relation.RevealAtUnitId, out var reveal)
                || !units.TryGetValue(source.FirstKnownAtUnitId, out var sourceFirst)
                || !units.TryGetValue(target.FirstKnownAtUnitId, out var targetFirst))
            {
                problems.Add("Uma relação aponta para conteúdo de outro livro.");
                continue;
            }
            if (reveal.OrderIndex < sourceFirst.OrderIndex
                || reveal.OrderIndex < targetFirst.OrderIndex)
                problems.Add("Uma relação aparece antes de um de seus participantes.");
        }
        return problems.Distinct().ToList();
    }

    private static void ValidateReveal(
        ICollection<string> problems,
        IReadOnlyDictionary<Guid, ReadingUnit> units,
        LoreEntity entity,
        Guid revealId,
        string label)
    {
        if (!units.TryGetValue(entity.FirstKnownAtUnitId, out var first)
            || !units.TryGetValue(revealId, out var reveal))
            problems.Add($"{label} aponta para uma unidade inválida.");
        else if (reveal.OrderIndex < first.OrderIndex)
            problems.Add($"{label} aparece antes da entidade.");
    }

    private static IResult InvalidPack(IEnumerable<string> problems) =>
        Results.Problem(string.Join(" ", problems),
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "O pacote ainda não pode ser publicado.");

    private static async Task<IResult?> ValidateUnitAsync(
        MnemoraDbContext db, Guid bookId, Guid? unitId, UnitWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)
            || string.IsNullOrWhiteSpace(request.SafeLabel)
            || string.IsNullOrWhiteSpace(request.Slug)
            || request.OrderIndex <= 0
            || !Enum.TryParse<ReadingUnitType>(request.Type, true, out _))
            return Results.BadRequest(
                "Informe título, rótulo seguro, slug, tipo e ordem positiva.");
        if (request.ParentUnitId is Guid parentId
            && !await db.ReadingUnits.AnyAsync(x =>
                x.Id == parentId && x.BookId == bookId))
            return Results.BadRequest("Unidade pai inválida.");
        if (await db.ReadingUnits.AnyAsync(x => x.BookId == bookId
            && x.Id != unitId && (x.OrderIndex == request.OrderIndex
                || x.Slug == request.Slug.Trim())))
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

    private static bool BreaksHierarchy(
        ReadingUnit unit, int newOrder, IReadOnlyList<ReadingUnit> units) =>
        (unit.ParentUnitId is Guid parentId
            && units.Any(x => x.Id == parentId && x.OrderIndex >= newOrder))
        || units.Any(x => x.ParentUnitId == unit.Id && x.OrderIndex <= newOrder);

    private static ReadingUnit ToUnit(Guid bookId, UnitWrite request) => new()
    {
        BookId = bookId,
        ParentUnitId = request.ParentUnitId,
        Title = request.Title.Trim(),
        SafeLabel = request.SafeLabel.Trim(),
        Slug = request.Slug.Trim(),
        OrderIndex = request.OrderIndex,
        Type = Enum.Parse<ReadingUnitType>(request.Type, true)
    };

    private static UnitDto ToDto(ReadingUnit unit) =>
        new(unit.Id, unit.BookId, unit.ParentUnitId, unit.Title, unit.SafeLabel,
            unit.Slug, unit.Type.ToString(), unit.OrderIndex);

    private static string Slug(string value, int index)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
                continue;
            normalized.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character) : '-');
        }
        var compact = string.Join('-', normalized.ToString()
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length > 140) compact = compact[..140].TrimEnd('-');
        return $"{(compact.Length == 0 ? "unidade" : compact)}-{index}";
    }

    private sealed record BookWrite(
        string Title, string Author, string? Description, string? CoverUrl);
    private sealed record UnitWrite(
        Guid? ParentUnitId, string Title, string SafeLabel,
        string Slug, string Type, int OrderIndex);
    private sealed record UnitDto(
        Guid Id, Guid BookId, Guid? ParentUnitId, string Title,
        string SafeLabel, string Slug, string Type, int OrderIndex);
    private sealed record OutlineImport(
        string? SourceUrl, string? Attribution,
        IReadOnlyList<OutlineImportItem> Items);
    private sealed record OutlineImportItem(string Title, int Level, int? Page);
    private sealed record AdminBookDto(
        Guid Id, string Title, string Author, string? Description, string? CoverUrl,
        string CatalogKind, string? ExternalProvider, string? ExternalId,
        string? Isbn10, string? Isbn13, string? Publisher,
        DateOnly? PublishedDate, string? Language,
        string? ReadingOutlineSourceUrl, string? ReadingOutlineAttribution,
        string ReadingOutlineStatus, string LoreStatus,
        DateTime? ReadingOutlinePublishedAt, DateTime? LorePublishedAt,
        int RequestCount, int UnitCount, int EntityCount, int FactCount,
        int RelationCount, int EventCount);
}
