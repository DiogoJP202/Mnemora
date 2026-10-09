using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mnemora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemoryPackWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LorePublishedAt",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoreStatus",
                table: "Books",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "ReadingOutlineAttribution",
                table: "Books",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadingOutlinePublishedAt",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadingOutlineSourceUrl",
                table: "Books",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadingOutlineStatus",
                table: "Books",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.Sql("""
                UPDATE "Books"
                SET "ReadingOutlineStatus" = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "ReadingUnits"
                            WHERE "ReadingUnits"."BookId" = "Books"."Id")
                        THEN 'Published' ELSE 'None' END,
                    "ReadingOutlinePublishedAt" = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "ReadingUnits"
                            WHERE "ReadingUnits"."BookId" = "Books"."Id")
                        THEN "UpdatedAt" ELSE NULL END,
                    "LoreStatus" = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "LoreEntities"
                            WHERE "LoreEntities"."BookId" = "Books"."Id")
                        THEN 'Published' ELSE 'None' END,
                    "LorePublishedAt" = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "LoreEntities"
                            WHERE "LoreEntities"."BookId" = "Books"."Id")
                        THEN "UpdatedAt" ELSE NULL END;
                """);

            migrationBuilder.CreateTable(
                name: "MemoryPackRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryPackRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryPackRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemoryPackRequests_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryPackRequests_BookId_CreatedAt",
                table: "MemoryPackRequests",
                columns: new[] { "BookId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryPackRequests_UserId_BookId",
                table: "MemoryPackRequests",
                columns: new[] { "UserId", "BookId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryPackRequests");

            migrationBuilder.DropColumn(
                name: "LorePublishedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "LoreStatus",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ReadingOutlineAttribution",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ReadingOutlinePublishedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ReadingOutlineSourceUrl",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ReadingOutlineStatus",
                table: "Books");
        }
    }
}
