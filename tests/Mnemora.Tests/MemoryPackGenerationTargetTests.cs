using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Mnemora.Application;
using Mnemora.Domain;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackGenerationTargetTests
{
    [Fact]
    public async Task Empty_draft_uses_export_identity_without_writing_editorial_data()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Gate.ReadAsync(fixture.Book.Id);
        Assert.Equal("As cartas", result.Title);
        Assert.Equal("Autora original", result.Author);
        Assert.Equal("9780000000001", result.Isbn13);
        Assert.Equal((await fixture.Transfer.ExportAsync(fixture.Book.Id))!.Book, result);
        Assert.Empty(await fixture.Db.ReadingUnits.ToListAsync());
        Assert.Empty(await fixture.Db.LoreEntities.ToListAsync());
        Assert.Equal(EditorialContentStatus.Draft, fixture.Book.LoreStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Publication_or_even_one_draft_unit_blocks_generation(bool published)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (published) fixture.Book.LoreStatus = EditorialContentStatus.Published;
        else fixture.Db.ReadingUnits.Add(new ReadingUnit
        {
            BookId = fixture.Book.Id, Title = "Um", SafeLabel = "Capítulo 1",
            Slug = "um", Type = ReadingUnitType.Chapter, OrderIndex = 1
        });
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GenerationException>(() => fixture.Gate.ReadAsync(fixture.Book.Id));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("book_has_content", error.Code);
    }

    [Fact]
    public async Task Private_or_missing_book_is_not_available_to_generator()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Book.CatalogKind = BookCatalogKind.Private;
        var ownerId = Guid.NewGuid();
        fixture.Db.Users.Add(new IdentityUser<Guid> { Id = ownerId, UserName = "private-owner" });
        fixture.Book.OwnerUserId = ownerId;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<GenerationException>(
            () => fixture.Gate.ReadAsync(fixture.Book.Id))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<GenerationException>(
            () => fixture.Gate.ReadAsync(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Changed_edition_invalidates_the_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var identity = await fixture.Gate.ReadAsync(fixture.Book.Id);
        fixture.Book.Isbn13 = "9780000000002";
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GenerationException>(() => fixture.Gate.ReadAsync(fixture.Book.Id, identity));
        Assert.Equal("edition_changed", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    private sealed class Fixture(SqliteConnection connection, MnemoraDbContext db, Book book) : IAsyncDisposable
    {
        public MnemoraDbContext Db { get; } = db;
        public Book Book { get; } = book;
        public MemoryPackTransfer Transfer { get; } = new(db);
        public MemoryPackGenerationTarget Gate => new(Db, Transfer);
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MnemoraDbContext(new DbContextOptionsBuilder<MnemoraDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var book = new Book
            {
                Title = " As cartas ", Author = " Autora original ", Isbn13 = "9780000000001",
                Description = "Não é fonte narrativa", ReadingOutlineStatus = EditorialContentStatus.Draft,
                LoreStatus = EditorialContentStatus.Draft
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return new(connection, db, book);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
