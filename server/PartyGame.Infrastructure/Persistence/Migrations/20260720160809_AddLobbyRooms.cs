using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLobbyRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false, collation: "NOCASE"),
                    Phase = table.Column<int>(type: "INTEGER", nullable: false),
                    StateVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    HostPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayConnected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NormalizedNickname = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    IsHost = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsConnected = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasProfilePhoto = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfilePhotoStorageKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ProfilePhotoContentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_GameRooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "GameRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoomSettings",
                columns: table => new
                {
                    GameRoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionsPerRound = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerSelectionSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TextAnswerSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    VotingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PhotoSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultPresentationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalRoundEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FinalDrawingPasses = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomSettings", x => x.GameRoomId);
                    table.ForeignKey(
                        name: "FK_RoomSettings_GameRooms_GameRoomId",
                        column: x => x.GameRoomId,
                        principalTable: "GameRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSessions",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReconnectTokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSessions", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_PlayerSessions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameRooms_Code",
                table: "GameRooms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_RoomId_NormalizedNickname",
                table: "Players",
                columns: new[] { "RoomId", "NormalizedNickname" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerSessions");

            migrationBuilder.DropTable(
                name: "RoomSettings");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "GameRooms");
        }
    }
}
