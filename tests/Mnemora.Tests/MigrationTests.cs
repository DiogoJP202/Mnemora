using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task Catalog_visibility_upgrade_preserves_seed_and_marks_legacy_imports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MnemoraDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MnemoraDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20260915180038_CoreDomain");

        var seedId = Guid.NewGuid();
        var importedId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Books"
                ("Id", "Title", "Author", "ExternalProvider", "ExternalId", "CreatedAt", "UpdatedAt")
            VALUES
                ({seedId}, {"O Arquivo da Neblina"}, {"Equipe Mnemora"},
                    {"MnemoraSeed"}, {"mist-archive-v1"}, {now}, {now}),
                ({importedId}, {"Importação antiga"}, {"Autoria externa"},
                    {"GoogleBooks"}, {"legacy-volume"}, {now}, {now});
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var books = await db.Books.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal(BookCatalogKind.Curated, books[seedId].CatalogKind);
        Assert.Null(books[seedId].OwnerUserId);
        Assert.Equal(BookCatalogKind.Imported, books[importedId].CatalogKind);
        Assert.Null(books[importedId].OwnerUserId);
    }
}
