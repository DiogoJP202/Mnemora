using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mnemora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookCatalogVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogKind",
                table: "Books",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Curated");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Books\" SET \"CatalogKind\" = 'Imported' "
                + "WHERE \"ExternalProvider\" = 'GoogleBooks' "
                + "AND \"ExternalId\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Books_CatalogKind_OwnerUserId",
                table: "Books",
                columns: new[] { "CatalogKind", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Books_OwnerUserId_Isbn10",
                table: "Books",
                columns: new[] { "OwnerUserId", "Isbn10" },
                unique: true,
                filter: "\"CatalogKind\" = 'Private' AND \"Isbn10\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Books_OwnerUserId_Isbn13",
                table: "Books",
                columns: new[] { "OwnerUserId", "Isbn13" },
                unique: true,
                filter: "\"CatalogKind\" = 'Private' AND \"Isbn13\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Books_CatalogOwner",
                table: "Books",
                sql: "(\"CatalogKind\" = 'Private' AND \"OwnerUserId\" IS NOT NULL) OR (\"CatalogKind\" <> 'Private' AND \"OwnerUserId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_AspNetUsers_OwnerUserId",
                table: "Books",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_AspNetUsers_OwnerUserId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_CatalogKind_OwnerUserId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_OwnerUserId_Isbn10",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_OwnerUserId_Isbn13",
                table: "Books");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Books_CatalogOwner",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CatalogKind",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Books");
        }
    }
}
