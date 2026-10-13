using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public sealed class MemoryPackTransfer(MnemoraDbContext db)
{
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<MemoryPackDocument?> ExportAsync(Guid bookId,
        CancellationToken cancellationToken = default)
    {
        var book = await SharedBookAsync(bookId, cancellationToken);
        return book is null ? null : await ExportBookAsync(book, cancellationToken);
    }

    public async Task<MemoryPackTransferOutcome?> ValidateAsync(Guid bookId,
        MemoryPackDocument document, CancellationToken cancellationToken = default)
    {
        var book = await SharedBookAsync(bookId, cancellationToken);
        return book is null ? null : await CheckAsync(book, document, cancellationToken);
    }

    public async Task<MemoryPackTransferOutcome?> ImportAsync(Guid bookId,
        MemoryPackDocument document, CancellationToken cancellationToken = default)
    {
        // The empty-target check and inserts share the same database transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var book = await db.Books.FirstOrDefaultAsync(x => x.Id == bookId
            && x.CatalogKind != BookCatalogKind.Private, cancellationToken);
        if (book is null) return null;
        var outcome = await CheckAsync(book, document, cancellationToken);
        if (!outcome.Validation.IsValid || outcome.Validation.AlreadyImported)
            return outcome;

        var normalized = Normalize(document);
        var units = normalized.ReadingUnits.ToDictionary(x => x.Slug, x => new ReadingUnit
        {
            BookId = bookId, Slug = x.Slug, Title = x.Title, SafeLabel = x.SafeLabel,
            Type = Enum.Parse<ReadingUnitType>(x.Type), OrderIndex = x.OrderIndex,
            Description = x.Description
        }, StringComparer.Ordinal);
        foreach (var unit in normalized.ReadingUnits)
            if (unit.ParentSlug is not null)
                units[unit.Slug].ParentUnitId = units[unit.ParentSlug].Id;
        db.ReadingUnits.AddRange(units.Values);
        await db.SaveChangesAsync(cancellationToken);

        var entities = normalized.Entities.ToDictionary(x => x.Slug, x => new LoreEntity
        {
            BookId = bookId, Slug = x.Slug, Type = Enum.Parse<LoreEntityType>(x.Type),
            Name = x.Name, ShortDescription = x.ShortDescription, ImageUrl = x.ImageUrl,
            FirstKnownAtUnitId = units[x.FirstKnownAtUnitSlug].Id,
            Importance = x.Importance, ChronologyIndex = x.ChronologyIndex
        }, StringComparer.Ordinal);
        db.LoreEntities.AddRange(entities.Values);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var entity in normalized.Entities)
        {
            var entityId = entities[entity.Slug].Id;
            db.EntityAliases.AddRange(entity.Aliases.Select(x => new EntityAlias
            {
                EntityId = entityId, Alias = x.Alias,
                RevealAtUnitId = units[x.RevealAtUnitSlug].Id
            }));
            db.LoreFacts.AddRange(entity.Facts.Select(x => new LoreFact
            {
                EntityId = entityId, Content = x.Content, MemoryHint = x.MemoryHint,
                Type = Enum.Parse<LoreFactType>(x.Type), Importance = x.Importance,
                RevealAtUnitId = units[x.RevealAtUnitSlug].Id
            }));
        }
        db.LoreRelations.AddRange(normalized.Relations.Select(x => new LoreRelation
        {
            BookId = bookId, SourceEntityId = entities[x.SourceEntitySlug].Id,
            TargetEntityId = entities[x.TargetEntitySlug].Id,
            RelationType = x.RelationType, Label = x.Label,
            RevealAtUnitId = units[x.RevealAtUnitSlug].Id
        }));
        book.ReadingOutlineStatus = EditorialContentStatus.Draft;
        book.LoreStatus = EditorialContentStatus.Draft;
        book.ReadingOutlinePublishedAt = null;
        book.LorePublishedAt = null;
        book.ReadingOutlineSourceUrl = normalized.Source?.Url;
        book.ReadingOutlineAttribution = normalized.Source?.Attribution;
        book.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private Task<Book?> SharedBookAsync(Guid bookId, CancellationToken cancellationToken) =>
        db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bookId
            && x.CatalogKind != BookCatalogKind.Private, cancellationToken);

    private async Task<MemoryPackTransferOutcome> CheckAsync(Book book,
        MemoryPackDocument document, CancellationToken cancellationToken)
    {
        var issues = ValidateDocument(document, Identity(book));
        var counts = Counts(document);
        if (issues.Count > 0)
            return new(new(false, false, counts, issues));

        var existing = await ExportBookAsync(book, cancellationToken);
        var hasContent = existing.ReadingUnits.Count > 0 || existing.Entities.Count > 0
            || existing.Relations.Count > 0;
        if (hasContent)
        {
            if (JsonSerializer.Serialize(Normalize(existing), CanonicalJson)
                == JsonSerializer.Serialize(Normalize(document), CanonicalJson))
                return new(new(true, true, counts, []));
            issues.Add(new("$", "Este livro já possui conteúdo editorial diferente. A importação não substitui unidades ou conhecimento existentes."));
            return new(new(false, false, counts, issues), Conflict: true);
        }
        if (book.ReadingOutlineStatus == EditorialContentStatus.Published
            || book.LoreStatus == EditorialContentStatus.Published)
        {
            issues.Add(new("$", "Coloque o índice e o conhecimento em rascunho antes de importar."));
            return new(new(false, false, counts, issues), Conflict: true);
        }
        return new(new(true, false, counts, []));
    }

    private async Task<MemoryPackDocument> ExportBookAsync(Book book,
        CancellationToken cancellationToken)
    {
        var units = await db.ReadingUnits.AsNoTracking().Where(x => x.BookId == book.Id)
            .OrderBy(x => x.OrderIndex).ToListAsync(cancellationToken);
        var entities = await db.LoreEntities.AsNoTracking().Where(x => x.BookId == book.Id)
            .OrderBy(x => x.Slug).ToListAsync(cancellationToken);
        var entityIds = entities.Select(x => x.Id).ToArray();
        var aliases = await db.EntityAliases.AsNoTracking().Where(x => entityIds.Contains(x.EntityId))
            .ToListAsync(cancellationToken);
        var facts = await db.LoreFacts.AsNoTracking().Where(x => entityIds.Contains(x.EntityId))
            .ToListAsync(cancellationToken);
        var relations = await db.LoreRelations.AsNoTracking().Where(x => x.BookId == book.Id)
            .ToListAsync(cancellationToken);
        var unitSlugs = units.ToDictionary(x => x.Id, x => x.Slug);
        var entitySlugs = entities.ToDictionary(x => x.Id, x => x.Slug);
        string UnitSlug(Guid id) => unitSlugs.GetValueOrDefault(id, "");
        string EntitySlug(Guid id) => entitySlugs.GetValueOrDefault(id, "");
        var source = Source(book.ReadingOutlineSourceUrl, book.ReadingOutlineAttribution);
        return Normalize(new(1, Identity(book), source,
            units.Select(x => new MemoryPackUnit(x.Slug,
                x.ParentUnitId is Guid parent ? UnitSlug(parent) : null,
                x.Title, x.SafeLabel, x.Type.ToString(), x.OrderIndex, x.Description)).ToArray(),
            entities.Select(x => new MemoryPackEntity(x.Slug, x.Type.ToString(), x.Name,
                x.ShortDescription, x.ImageUrl, UnitSlug(x.FirstKnownAtUnitId),
                x.Importance, x.ChronologyIndex,
                aliases.Where(a => a.EntityId == x.Id).Select(a =>
                    new MemoryPackAlias(a.Alias, UnitSlug(a.RevealAtUnitId))).ToArray(),
                facts.Where(f => f.EntityId == x.Id).Select(f =>
                    new MemoryPackFact(f.Content, f.MemoryHint, f.Type.ToString(),
                        f.Importance, UnitSlug(f.RevealAtUnitId))).ToArray())).ToArray(),
            relations.Select(x => new MemoryPackRelation(EntitySlug(x.SourceEntityId),
                EntitySlug(x.TargetEntityId), x.RelationType, x.Label,
                UnitSlug(x.RevealAtUnitId))).ToArray()));
    }

    private static MemoryPackBook Identity(Book book) => new(book.Title.Trim(), book.Author.Trim(),
        Optional(book.ExternalProvider), Optional(book.ExternalId), Optional(book.Isbn10),
        Optional(book.Isbn13), Optional(book.Language));

    public static MemoryPackCounts Counts(MemoryPackDocument? document)
    {
        var entities = document?.Entities?.Where(x => x is not null).ToArray() ?? [];
        return new(document?.ReadingUnits?.Count ?? 0, document?.Entities?.Count ?? 0,
            entities.Sum(x => x.Aliases?.Count ?? 0), entities.Sum(x => x.Facts?.Count ?? 0),
            document?.Relations?.Count ?? 0,
            entities.Count(x => string.Equals(x.Type?.Trim(), "Event", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<MemoryPackIssue> ValidateDocument(MemoryPackDocument document,
        MemoryPackBook expectedBook)
    {
        var issues = new List<MemoryPackIssue>();
        void Issue(string path, string message) => issues.Add(new(path, message));
        void Text(string? value, string path, int max, bool required = false)
        {
            if (required && string.IsNullOrWhiteSpace(value)) Issue(path, "Campo obrigatório.");
            else if (value is not null && value.Trim().Length > max)
                Issue(path, $"Use no máximo {max} caracteres.");
        }
        void Slug(string? value, string path)
        {
            Text(value, path, 160, true);
            if (!string.IsNullOrWhiteSpace(value) && !SlugPattern.IsMatch(value.Trim()))
                Issue(path, "Use letras minúsculas, números e hífens no slug.");
        }
        void HttpsUrl(string? value, string path)
        {
            Text(value, path, 2_000);
            if (!string.IsNullOrWhiteSpace(value) && (!Uri.TryCreate(value.Trim(),
                UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0))
                Issue(path, "Use uma URL HTTPS absoluta, sem credenciais.");
        }
        void Type<T>(string? value, string path) where T : struct, Enum
        {
            if (value is null || !Enum.GetNames<T>().Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
                Issue(path, "Tipo nomeado inválido.");
        }
        if (document.SchemaVersion != 1) Issue("schemaVersion", "Apenas a versão 1 é aceita.");
        if (document.Book is null) Issue("book", "Informe a identidade do livro.");
        else
        {
            Text(document.Book.Title, "book.title", 300, true);
            Text(document.Book.Author, "book.author", 300, true);
            Text(document.Book.ExternalProvider, "book.externalProvider", 80);
            Text(document.Book.ExternalId, "book.externalId", 200);
            Text(document.Book.Isbn10, "book.isbn10", 10);
            Text(document.Book.Isbn13, "book.isbn13", 13);
            Text(document.Book.Language, "book.language", 80);
            var supplied = document.Book;
            var fields = new (string Path, string? Actual, string? Expected)[]
            {
                ("title", supplied.Title, expectedBook.Title),
                ("author", supplied.Author, expectedBook.Author),
                ("externalProvider", supplied.ExternalProvider, expectedBook.ExternalProvider),
                ("externalId", supplied.ExternalId, expectedBook.ExternalId),
                ("isbn10", supplied.Isbn10, expectedBook.Isbn10),
                ("isbn13", supplied.Isbn13, expectedBook.Isbn13),
                ("language", supplied.Language, expectedBook.Language)
            };
            foreach (var field in fields)
                if (!string.Equals(Optional(field.Actual), Optional(field.Expected), StringComparison.Ordinal))
                    Issue($"book.{field.Path}", "O campo precisa corresponder exatamente ao livro e à edição de destino. Exporte o modelo deste livro.");
        }
        if (document.Source is not null)
        {
            HttpsUrl(document.Source.Url, "source.url");
            Text(document.Source.Attribution, "source.attribution", 300);
        }
        if (document.ReadingUnits is not { Count: > 0 and <= 500 })
            Issue("readingUnits", "Informe entre 1 e 500 unidades de leitura.");
        if (document.Entities is not { Count: > 0 and <= 500 })
            Issue("entities", "Informe entre 1 e 500 entidades.");
        if (document.Relations is null || document.Relations.Count > 5_000)
            Issue("relations", "Informe uma lista com até 5000 relações.");
        if (document.ReadingUnits is null || document.Entities is null || document.Relations is null)
            return issues;
        if (document.ReadingUnits.Count > 500 || document.Entities.Count > 500
            || document.Relations.Count > 5_000) return issues;

        var units = new Dictionary<string, MemoryPackUnit>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        for (var index = 0; index < document.ReadingUnits.Count; index++)
        {
            var unit = document.ReadingUnits[index];
            var path = $"readingUnits[{index}]";
            if (unit is null) { Issue(path, "Unidade inválida."); continue; }
            Slug(unit.Slug, $"{path}.slug");
            if (!string.IsNullOrWhiteSpace(unit.Slug) && !units.TryAdd(unit.Slug.Trim(), unit))
                Issue($"{path}.slug", "O slug precisa ser único entre as unidades.");
            Text(unit.Title, $"{path}.title", 300, true);
            Text(unit.SafeLabel, $"{path}.safeLabel", 100, true);
            Text(unit.Description, $"{path}.description", 2_000);
            Type<ReadingUnitType>(unit.Type, $"{path}.type");
            if (unit.OrderIndex <= 0 || !orders.Add(unit.OrderIndex))
                Issue($"{path}.orderIndex", "A ordem precisa ser positiva e única.");
        }
        for (var index = 0; index < document.ReadingUnits.Count; index++)
        {
            var unit = document.ReadingUnits[index];
            if (unit is null || string.IsNullOrWhiteSpace(unit.ParentSlug)) continue;
            if (!units.TryGetValue(unit.ParentSlug.Trim(), out var parent))
                Issue($"readingUnits[{index}].parentSlug", "Unidade pai inexistente.");
            else if (parent.OrderIndex >= unit.OrderIndex)
                Issue($"readingUnits[{index}].parentSlug", "A unidade pai deve vir antes da filha, sem ciclos.");
        }
        MemoryPackUnit? Reveal(string? slug, string path, MemoryPackUnit? first = null)
        {
            if (string.IsNullOrWhiteSpace(slug) || !units.TryGetValue(slug.Trim(), out var unit))
            { Issue(path, "Unidade de revelação inexistente."); return null; }
            if (first is not null && unit.OrderIndex < first.OrderIndex)
                Issue(path, "A revelação não pode ocorrer antes da entidade.");
            return unit;
        }
        var entities = new Dictionary<string, MemoryPackEntity>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < document.Entities.Count; index++)
        {
            var entity = document.Entities[index];
            var path = $"entities[{index}]";
            if (entity is null) { Issue(path, "Entidade inválida."); continue; }
            Slug(entity.Slug, $"{path}.slug");
            if (!string.IsNullOrWhiteSpace(entity.Slug) && !entities.TryAdd(entity.Slug.Trim(), entity))
                Issue($"{path}.slug", "O slug precisa ser único entre as entidades.");
            Type<LoreEntityType>(entity.Type, $"{path}.type");
            Text(entity.Name, $"{path}.name", 200, true);
            Text(entity.ShortDescription, $"{path}.shortDescription", 1_000);
            HttpsUrl(entity.ImageUrl, $"{path}.imageUrl");
            if (entity.Importance < 0) Issue($"{path}.importance", "A importância não pode ser negativa.");
            var first = Reveal(entity.FirstKnownAtUnitSlug, $"{path}.firstKnownAtUnitSlug");
            if (entity.Aliases is null || entity.Aliases.Count > 20)
                Issue($"{path}.aliases", "Informe uma lista com até 20 aliases.");
            else
            {
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var aliasIndex = 0; aliasIndex < entity.Aliases.Count; aliasIndex++)
                {
                    var alias = entity.Aliases[aliasIndex];
                    var aliasPath = $"{path}.aliases[{aliasIndex}]";
                    if (alias is null) { Issue(aliasPath, "Alias inválido."); continue; }
                    Text(alias.Alias, $"{aliasPath}.alias", 200, true);
                    if (!string.IsNullOrWhiteSpace(alias.Alias) && !aliases.Add(alias.Alias.Trim()))
                        Issue($"{aliasPath}.alias", "O alias precisa ser único na entidade.");
                    Reveal(alias.RevealAtUnitSlug, $"{aliasPath}.revealAtUnitSlug", first);
                }
            }
            if (entity.Facts is null || entity.Facts.Count > 100)
                Issue($"{path}.facts", "Informe uma lista com até 100 fatos.");
            else
                for (var factIndex = 0; factIndex < entity.Facts.Count; factIndex++)
                {
                    var fact = entity.Facts[factIndex];
                    var factPath = $"{path}.facts[{factIndex}]";
                    if (fact is null) { Issue(factPath, "Fato inválido."); continue; }
                    Text(fact.Content, $"{factPath}.content", 4_000, true);
                    Text(fact.MemoryHint, $"{factPath}.memoryHint", 1_000);
                    Type<LoreFactType>(fact.Type, $"{factPath}.type");
                    if (fact.Importance < 0) Issue($"{factPath}.importance", "A importância não pode ser negativa.");
                    Reveal(fact.RevealAtUnitSlug, $"{factPath}.revealAtUnitSlug", first);
                }
        }
        for (var index = 0; index < document.Relations.Count; index++)
        {
            var relation = document.Relations[index];
            var path = $"relations[{index}]";
            if (relation is null) { Issue(path, "Relação inválida."); continue; }
            Text(relation.RelationType, $"{path}.relationType", 80, true);
            Text(relation.Label, $"{path}.label", 300);
            var reveal = Reveal(relation.RevealAtUnitSlug, $"{path}.revealAtUnitSlug");
            foreach (var reference in new[]
            {
                (Field: "sourceEntitySlug", Slug: relation.SourceEntitySlug),
                (Field: "targetEntitySlug", Slug: relation.TargetEntitySlug)
            })
            {
                if (string.IsNullOrWhiteSpace(reference.Slug)
                    || !entities.TryGetValue(reference.Slug.Trim(), out var participant))
                    Issue($"{path}.{reference.Field}", "Entidade participante inexistente.");
                else if (reveal is not null && !string.IsNullOrWhiteSpace(participant.FirstKnownAtUnitSlug)
                    && units.TryGetValue(participant.FirstKnownAtUnitSlug.Trim(), out var first)
                    && reveal.OrderIndex < first.OrderIndex)
                    Issue($"{path}.revealAtUnitSlug", "A relação não pode ocorrer antes dos dois participantes.");
            }
        }
        return issues;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static MemoryPackSource? Source(string? url, string? attribution) =>
        Optional(url) is null && Optional(attribution) is null
            ? null : new(Optional(url), Optional(attribution));
    private static string Ref(string value) => value.Trim().ToLowerInvariant();
    private static string Named<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Trim(), true, out var result) ? result.ToString() : value.Trim();

    private static MemoryPackDocument Normalize(MemoryPackDocument document) => new(1,
        new(document.Book.Title.Trim(), document.Book.Author.Trim(),
            Optional(document.Book.ExternalProvider), Optional(document.Book.ExternalId),
            Optional(document.Book.Isbn10), Optional(document.Book.Isbn13), Optional(document.Book.Language)),
        Source(document.Source?.Url, document.Source?.Attribution),
        document.ReadingUnits.Select(x => new MemoryPackUnit(Ref(x.Slug),
            Optional(x.ParentSlug) is string parent ? Ref(parent) : null,
            x.Title.Trim(), x.SafeLabel.Trim(), Named<ReadingUnitType>(x.Type),
            x.OrderIndex, Optional(x.Description)))
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Slug, StringComparer.Ordinal).ToArray(),
        document.Entities.Select(x => new MemoryPackEntity(Ref(x.Slug), Named<LoreEntityType>(x.Type),
            x.Name.Trim(), Optional(x.ShortDescription), Optional(x.ImageUrl),
            Ref(x.FirstKnownAtUnitSlug), x.Importance, x.ChronologyIndex,
            x.Aliases.Select(a => new MemoryPackAlias(a.Alias.Trim(), Ref(a.RevealAtUnitSlug)))
                .OrderBy(a => a.Alias, StringComparer.Ordinal).ThenBy(a => a.RevealAtUnitSlug, StringComparer.Ordinal).ToArray(),
            x.Facts.Select(f => new MemoryPackFact(f.Content.Trim(), Optional(f.MemoryHint),
                Named<LoreFactType>(f.Type), f.Importance, Ref(f.RevealAtUnitSlug)))
                .OrderBy(f => JsonSerializer.Serialize(f, CanonicalJson), StringComparer.Ordinal).ToArray()))
            .OrderBy(x => x.Slug, StringComparer.Ordinal).ToArray(),
        document.Relations.Select(x => new MemoryPackRelation(Ref(x.SourceEntitySlug),
            Ref(x.TargetEntitySlug), x.RelationType.Trim(), Optional(x.Label), Ref(x.RevealAtUnitSlug)))
            .OrderBy(x => JsonSerializer.Serialize(x, CanonicalJson), StringComparer.Ordinal).ToArray());
}
