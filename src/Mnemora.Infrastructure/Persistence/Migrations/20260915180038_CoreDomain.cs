using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mnemora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    Author = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Isbn10 = table.Column<string>(type: "TEXT", nullable: true),
                    Isbn13 = table.Column<string>(type: "TEXT", nullable: true),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    PublishedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalProvider = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SafeLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingUnits_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingUnits_ReadingUnits_ParentUnitId",
                        column: x => x.ParentUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoreEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ShortDescription = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FirstKnownAtUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Importance = table.Column<int>(type: "INTEGER", nullable: false),
                    ChronologyIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoreEntities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoreEntities_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoreEntities_ReadingUnits_FirstKnownAtUnitId",
                        column: x => x.FirstKnownAtUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CurrentReadingUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentPage = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBooks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBooks_ReadingUnits_CurrentReadingUnitId",
                        column: x => x.CurrentReadingUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntityAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RevealAtUnitId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntityAliases_LoreEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityAliases_ReadingUnits_RevealAtUnitId",
                        column: x => x.RevealAtUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoreFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    MemoryHint = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Importance = table.Column<int>(type: "INTEGER", nullable: false),
                    RevealAtUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoreFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoreFacts_LoreEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoreFacts_ReadingUnits_RevealAtUnitId",
                        column: x => x.RevealAtUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoreRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    RevealAtUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoreRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoreRelations_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoreRelations_LoreEntities_SourceEntityId",
                        column: x => x.SourceEntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoreRelations_LoreEntities_TargetEntityId",
                        column: x => x.TargetEntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoreRelations_ReadingUnits_RevealAtUnitId",
                        column: x => x.RevealAtUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReadingUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotes_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotes_LoreEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotes_ReadingUnits_ReadingUnitId",
                        column: x => x.ReadingUnitId,
                        principalTable: "ReadingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRecallActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: true),
                    Remembered = table.Column<bool>(type: "INTEGER", nullable: true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecallActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecallActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRecallActivities_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRecallActivities_LoreEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "LoreEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_ExternalProvider_ExternalId",
                table: "Books",
                columns: new[] { "ExternalProvider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityAliases_Alias",
                table: "EntityAliases",
                column: "Alias");

            migrationBuilder.CreateIndex(
                name: "IX_EntityAliases_EntityId",
                table: "EntityAliases",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_EntityAliases_RevealAtUnitId",
                table: "EntityAliases",
                column: "RevealAtUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreEntities_BookId_Slug",
                table: "LoreEntities",
                columns: new[] { "BookId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoreEntities_BookId_Type",
                table: "LoreEntities",
                columns: new[] { "BookId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_LoreEntities_FirstKnownAtUnitId",
                table: "LoreEntities",
                column: "FirstKnownAtUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreFacts_EntityId",
                table: "LoreFacts",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreFacts_RevealAtUnitId",
                table: "LoreFacts",
                column: "RevealAtUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreRelations_BookId_SourceEntityId",
                table: "LoreRelations",
                columns: new[] { "BookId", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_LoreRelations_BookId_TargetEntityId",
                table: "LoreRelations",
                columns: new[] { "BookId", "TargetEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_LoreRelations_RevealAtUnitId",
                table: "LoreRelations",
                column: "RevealAtUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreRelations_SourceEntityId",
                table: "LoreRelations",
                column: "SourceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_LoreRelations_TargetEntityId",
                table: "LoreRelations",
                column: "TargetEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingUnits_BookId_OrderIndex",
                table: "ReadingUnits",
                columns: new[] { "BookId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingUnits_BookId_Slug",
                table: "ReadingUnits",
                columns: new[] { "BookId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingUnits_ParentUnitId",
                table: "ReadingUnits",
                column: "ParentUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_BookId",
                table: "UserBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_CurrentReadingUnitId",
                table: "UserBooks",
                column: "CurrentReadingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_BookId",
                table: "UserBooks",
                columns: new[] { "UserId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_BookId",
                table: "UserNotes",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_EntityId",
                table: "UserNotes",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_ReadingUnitId",
                table: "UserNotes",
                column: "ReadingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UserId_BookId",
                table: "UserNotes",
                columns: new[] { "UserId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRecallActivities_BookId",
                table: "UserRecallActivities",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecallActivities_EntityId",
                table: "UserRecallActivities",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecallActivities_UserId_BookId_At",
                table: "UserRecallActivities",
                columns: new[] { "UserId", "BookId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityAliases");

            migrationBuilder.DropTable(
                name: "LoreFacts");

            migrationBuilder.DropTable(
                name: "LoreRelations");

            migrationBuilder.DropTable(
                name: "UserBooks");

            migrationBuilder.DropTable(
                name: "UserNotes");

            migrationBuilder.DropTable(
                name: "UserRecallActivities");

            migrationBuilder.DropTable(
                name: "LoreEntities");

            migrationBuilder.DropTable(
                name: "ReadingUnits");

            migrationBuilder.DropTable(
                name: "Books");
        }
    }
}
