using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

public sealed class MnemoraDbContext(DbContextOptions<MnemoraDbContext> options)
    : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<ReadingUnit> ReadingUnits => Set<ReadingUnit>();
    public DbSet<UserBook> UserBooks => Set<UserBook>();
    public DbSet<LoreEntity> LoreEntities => Set<LoreEntity>();
    public DbSet<EntityAlias> EntityAliases => Set<EntityAlias>();
    public DbSet<LoreFact> LoreFacts => Set<LoreFact>();
    public DbSet<LoreRelation> LoreRelations => Set<LoreRelation>();
    public DbSet<UserNote> UserNotes => Set<UserNote>();
    public DbSet<UserRecallActivity> UserRecallActivities => Set<UserRecallActivity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Book>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.Author).HasMaxLength(300);
            entity.Property(x => x.CatalogKind).HasConversion<string>().HasMaxLength(30)
                .HasDefaultValue(BookCatalogKind.Curated);
            entity.Property(x => x.ExternalProvider).HasMaxLength(80);
            entity.Property(x => x.ExternalId).HasMaxLength(200);
            entity.HasIndex(x => new { x.ExternalProvider, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.CatalogKind, x.OwnerUserId });
            entity.HasIndex(x => new { x.OwnerUserId, x.Isbn10 }).IsUnique()
                .HasFilter("\"CatalogKind\" = 'Private' AND \"Isbn10\" IS NOT NULL");
            entity.HasIndex(x => new { x.OwnerUserId, x.Isbn13 }).IsUnique()
                .HasFilter("\"CatalogKind\" = 'Private' AND \"Isbn13\" IS NOT NULL");
            entity.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint("CK_Books_CatalogOwner",
                "(\"CatalogKind\" = 'Private' AND \"OwnerUserId\" IS NOT NULL) OR "
                + "(\"CatalogKind\" <> 'Private' AND \"OwnerUserId\" IS NULL)"));
        });
        builder.Entity<ReadingUnit>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.SafeLabel).HasMaxLength(100);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.HasIndex(x => new { x.BookId, x.OrderIndex }).IsUnique();
            entity.HasIndex(x => new { x.BookId, x.Slug }).IsUnique();
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.ParentUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserBook>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.UserId, x.BookId }).IsUnique();
            entity.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.UserId);
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.CurrentReadingUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LoreEntity>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.HasIndex(x => new { x.BookId, x.Slug }).IsUnique();
            entity.HasIndex(x => new { x.BookId, x.Type });
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.FirstKnownAtUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EntityAlias>(entity =>
        {
            entity.Property(x => x.Alias).HasMaxLength(200);
            entity.HasIndex(x => x.Alias);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.EntityId);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.RevealAtUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LoreFact>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => x.EntityId);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.EntityId);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.RevealAtUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LoreRelation>(entity =>
        {
            entity.Property(x => x.RelationType).HasMaxLength(80);
            entity.HasIndex(x => new { x.BookId, x.SourceEntityId });
            entity.HasIndex(x => new { x.BookId, x.TargetEntityId });
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.SourceEntityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.TargetEntityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.RevealAtUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserNote>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.BookId });
            entity.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.UserId);
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.EntityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReadingUnit>().WithMany().HasForeignKey(x => x.ReadingUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserRecallActivity>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.BookId, x.At });
            entity.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.UserId);
            entity.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId);
            entity.HasOne<LoreEntity>().WithMany().HasForeignKey(x => x.EntityId);
        });
    }
}
