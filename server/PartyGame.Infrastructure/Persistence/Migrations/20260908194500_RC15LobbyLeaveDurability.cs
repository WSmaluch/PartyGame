using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PartyGameDbContext))]
[Migration("20260908194500_RC15LobbyLeaveDurability")]
public partial class RC15LobbyLeaveDurability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Players",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.DropIndex(
            name: "IX_Players_RoomId_NormalizedNickname",
            table: "Players");

        migrationBuilder.CreateIndex(
            name: "IX_Players_RoomId_NormalizedNickname",
            table: "Players",
            columns: new[] { "RoomId", "NormalizedNickname" },
            unique: true,
            filter: "\"IsActive\" = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Players_RoomId_NormalizedNickname",
            table: "Players");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Players");

        migrationBuilder.CreateIndex(
            name: "IX_Players_RoomId_NormalizedNickname",
            table: "Players",
            columns: new[] { "RoomId", "NormalizedNickname" },
            unique: true);
    }
}
