using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public static class SeedData
{
    public static async Task RunAsync(
        IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var db = services.GetRequiredService<MnemoraDbContext>();
        var users = services.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var adminEmail = configuration["ADMIN_EMAIL"]?.Trim();
        var adminPassword = configuration["ADMIN_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            if (!await roles.RoleExistsAsync("Admin"))
                EnsureSucceeded(
                    await roles.CreateAsync(new IdentityRole<Guid>("Admin")),
                    "criar a role Admin");
            var admin = await users.FindByEmailAsync(adminEmail);
            if (admin is null)
            {
                admin = new IdentityUser<Guid> { UserName = adminEmail, Email = adminEmail };
                var created = await users.CreateAsync(admin, adminPassword);
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        "Não foi possível criar o Admin. Verifique ADMIN_EMAIL e ADMIN_PASSWORD.");
            }
            else if (!await users.IsInRoleAsync(admin, "Admin")
                     && !await users.CheckPasswordAsync(admin, adminPassword))
            {
                throw new InvalidOperationException(
                    "ADMIN_EMAIL já pertence a uma conta sem a role Admin e a senha configurada não corresponde.");
            }

            if (!await users.IsInRoleAsync(admin, "Admin"))
                EnsureSucceeded(await users.AddToRoleAsync(admin, "Admin"),
                    "atribuir a role Admin");
        }
        else
        {
            logger.LogWarning("Admin de desenvolvimento não criado: configure ADMIN_EMAIL e ADMIN_PASSWORD.");
        }

        if (await db.Books.AnyAsync(x =>
                x.ExternalProvider == "MnemoraSeed" && x.ExternalId == "mist-archive-v1"))
        {
            logger.LogInformation("Pack demonstrativo já existe.");
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var book = new Book
        {
            Title = "O Arquivo da Neblina",
            Author = "Equipe Mnemora",
            Description = "Uma viajante segue marcas deixadas em uma cidade coberta de névoa.",
            Language = "pt-BR",
            ExternalProvider = "MnemoraSeed",
            ExternalId = "mist-archive-v1"
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var part = Unit(book.Id, 5, "Primeira travessia", "Parte 1", "parte-1",
            ReadingUnitType.Part);
        var u10 = Unit(book.Id, 10, "A carta", "Capítulo 1", "capitulo-1",
            ReadingUnitType.Chapter, part.Id);
        var u20 = Unit(book.Id, 20, "A ponte", "Capítulo 2", "capitulo-2",
            ReadingUnitType.Chapter, part.Id);
        var u30 = Unit(book.Id, 30, "A chave", "Capítulo 3", "capitulo-3",
            ReadingUnitType.Chapter, part.Id);
        var u40 = Unit(book.Id, 40, "O farol", "Capítulo 4", "capitulo-4",
            ReadingUnitType.Chapter, part.Id);
        db.ReadingUnits.AddRange(part, u10, u20, u30, u40);
        await db.SaveChangesAsync();

        var nara = Entity(book.Id, u10.Id, "Nara", "nara",
            LoreEntityType.Character, "Viajante que carrega uma carta sem remetente.", 5);
        var cael = Entity(book.Id, u20.Id, "Cael", "cael",
            LoreEntityType.Character, "Cartógrafo que registra caminhos pela névoa.", 4);
        var iria = Entity(book.Id, u40.Id, "Iria", "iria",
            LoreEntityType.Character, "Guardadora do farol.", 3);
        var bridge = Entity(book.Id, u20.Id, "Ponte das Marcas", "ponte-das-marcas",
            LoreEntityType.Location, "Travessia marcada com símbolos antigos.", 3);
        var lighthouse = Entity(book.Id, u40.Id, "Farol da Névoa", "farol-da-nevoa",
            LoreEntityType.Location, "Farol sobre a costa encoberta.", 3);
        var faction = Entity(book.Id, u30.Id, "Círculo dos Cartógrafos",
            "circulo-dos-cartografos", LoreEntityType.Faction,
            "Grupo que reúne registros de caminhos.", 3);
        var crossing = Entity(book.Id, u20.Id, "A travessia da ponte",
            "travessia-da-ponte", LoreEntityType.Event,
            "Nara atravessa a ponte com Cael.", 2);
        crossing.ChronologyIndex = 20;
        var beacon = Entity(book.Id, u40.Id, "A luz do farol",
            "luz-do-farol", LoreEntityType.Event,
            "O farol volta a iluminar a costa.", 2);
        beacon.ChronologyIndex = 40;
        db.LoreEntities.AddRange(nara, cael, iria, bridge, lighthouse,
            faction, crossing, beacon);
        await db.SaveChangesAsync();

        db.EntityAliases.AddRange(
            Alias(nara.Id, u10.Id, "A viajante"),
            Alias(nara.Id, u40.Id, "Guardadora da luz"),
            Alias(cael.Id, u20.Id, "O cartógrafo"));
        db.LoreFacts.AddRange(
            Fact(nara.Id, u10.Id, "Nara guarda uma carta sem remetente.",
                "A viajante da carta sem remetente.", 5),
            Fact(nara.Id, u20.Id, "Nara atravessa a Ponte das Marcas com Cael.", null, 3),
            Fact(nara.Id, u40.Id, "Nara encontra Iria no farol.", null, 3),
            Fact(cael.Id, u20.Id, "Cael registra caminhos pela névoa.",
                "O cartógrafo que acompanha Nara na ponte.", 4),
            Fact(iria.Id, u40.Id, "Iria cuida do Farol da Névoa.",
                "A guardadora do farol.", 4),
            Fact(faction.Id, u30.Id, "O Círculo reúne mapas feitos por Cael.", null, 2));
        db.LoreRelations.AddRange(
            Relation(book.Id, nara.Id, cael.Id, u20.Id, "TravelsWith", "Viajam juntos"),
            Relation(book.Id, cael.Id, faction.Id, u30.Id, "MemberOf", "Integra"),
            Relation(book.Id, nara.Id, iria.Id, u40.Id, "Meets", "Encontra"));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        logger.LogInformation("Pack demonstrativo original criado: {BookId}", book.Id);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;
        var errors = string.Join("; ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException(
            $"Não foi possível {operation}. Erros do Identity: {errors}.");
    }

    private static ReadingUnit Unit(Guid bookId, int order, string title, string safeLabel,
        string slug, ReadingUnitType type, Guid? parentId = null) => new()
    {
        BookId = bookId, OrderIndex = order, Title = title,
        SafeLabel = safeLabel, Slug = slug, Type = type, ParentUnitId = parentId
    };

    private static LoreEntity Entity(Guid bookId, Guid firstUnitId, string name,
        string slug, LoreEntityType type, string summary, int importance) => new()
    {
        BookId = bookId, FirstKnownAtUnitId = firstUnitId,
        Name = name, Slug = slug, Type = type,
        ShortDescription = summary, Importance = importance
    };

    private static EntityAlias Alias(Guid entityId, Guid revealId, string text) => new()
    {
        EntityId = entityId, RevealAtUnitId = revealId, Alias = text
    };

    private static LoreFact Fact(Guid entityId, Guid revealId,
        string content, string? hint, int importance) => new()
    {
        EntityId = entityId, RevealAtUnitId = revealId, Content = content,
        MemoryHint = hint, Importance = importance, Type = LoreFactType.Description
    };

    private static LoreRelation Relation(Guid bookId, Guid sourceId, Guid targetId,
        Guid revealId, string type, string label) => new()
    {
        BookId = bookId, SourceEntityId = sourceId, TargetEntityId = targetId,
        RevealAtUnitId = revealId, RelationType = type, Label = label
    };
}
