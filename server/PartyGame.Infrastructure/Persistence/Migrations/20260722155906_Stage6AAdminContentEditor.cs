using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage6AAdminContentEditor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameQuestions_CategoryId",
                table: "GameQuestions");

            migrationBuilder.DropIndex(
                name: "IX_GameQuestions_Key",
                table: "GameQuestions");

            migrationBuilder.DropIndex(
                name: "IX_GamePackages_Key",
                table: "GamePackages");

            migrationBuilder.DropIndex(
                name: "IX_GameCategories_Key",
                table: "GameCategories");

            migrationBuilder.DropIndex(
                name: "IX_GameCategories_PackageId",
                table: "GameCategories");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentPackageVersionId",
                table: "GameRooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "GameQuestions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAtUtc",
                table: "GamePackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "GamePackages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "LogicalPackageId",
                table: "GamePackages",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "GamePackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "GamePackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "GamePackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "GameCategories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_GameRooms_ContentPackageVersionId",
                table: "GameRooms",
                column: "ContentPackageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestions_CategoryId_Key",
                table: "GameQuestions",
                columns: new[] { "CategoryId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamePackages_LogicalPackageId_Version",
                table: "GamePackages",
                columns: new[] { "LogicalPackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameCategories_PackageId_Key",
                table: "GameCategories",
                columns: new[] { "PackageId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GameRooms_GamePackages_ContentPackageVersionId",
                table: "GameRooms",
                column: "ContentPackageVersionId",
                principalTable: "GamePackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
                UPDATE GamePackages
                SET LogicalPackageId = '11111111-1111-1111-1111-111111111111',
                    Version = 1,
                    Status = 1,
                    PublishedAtUtc = CURRENT_TIMESTAMP,
                    ConcurrencyToken = lower(hex(randomblob(16)))
                WHERE LogicalPackageId = '00000000-0000-0000-0000-000000000000';
            ");

            migrationBuilder.Sql(@"
                UPDATE GameRooms
                SET ContentPackageVersionId = (SELECT Id FROM GamePackages LIMIT 1)
                WHERE ContentPackageVersionId = '00000000-0000-0000-0000-000000000000' AND EXISTS (SELECT 1 FROM GamePackages);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameRooms_GamePackages_ContentPackageVersionId",
                table: "GameRooms");

            migrationBuilder.DropIndex(
                name: "IX_GameRooms_ContentPackageVersionId",
                table: "GameRooms");

            migrationBuilder.DropIndex(
                name: "IX_GameQuestions_CategoryId_Key",
                table: "GameQuestions");

            migrationBuilder.DropIndex(
                name: "IX_GamePackages_LogicalPackageId_Version",
                table: "GamePackages");

            migrationBuilder.DropIndex(
                name: "IX_GameCategories_PackageId_Key",
                table: "GameCategories");

            migrationBuilder.DropColumn(
                name: "ContentPackageVersionId",
                table: "GameRooms");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "GameQuestions");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "LogicalPackageId",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "GamePackages");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "GameCategories");

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestions_CategoryId",
                table: "GameQuestions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestions_Key",
                table: "GameQuestions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamePackages_Key",
                table: "GamePackages",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameCategories_Key",
                table: "GameCategories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameCategories_PackageId",
                table: "GameCategories",
                column: "PackageId");
        }
    }
}
