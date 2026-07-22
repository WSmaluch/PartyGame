using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SelectedPackageKeys",
                table: "GameRooms",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GamePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NamePl = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionPl = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentRoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentQuestionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionsInCurrentRound = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentQuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StageStartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StageEndsAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PausedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PausedStage = table.Column<int>(type: "INTEGER", nullable: true),
                    PausedRemainingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSessions_GameRooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "GameRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NamePl = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionPl = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameCategories_GamePackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "GamePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TextPl = table.Column<string>(type: "TEXT", nullable: false),
                    TextEn = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameQuestions_GameCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GameCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameRounds_GameCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GameCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameRounds_GameSessions_GameSessionId",
                        column: x => x.GameSessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameQuestionInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AnsweringStartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AnsweringEndsAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResultsStartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameQuestionInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameQuestionInstances_GameQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "GameQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameQuestionInstances_GameRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "GameRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameQuestionEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameQuestionEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameQuestionEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameQuestionEligiblePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSelectionAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoterPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSelectionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSelectionAnswers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerSelectionAnswers_Players_SelectedPlayerId",
                        column: x => x.SelectedPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerSelectionAnswers_Players_VoterPlayerId",
                        column: x => x.VoterPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScoreTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoreTransactions_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreTransactions_GameSessions_GameSessionId",
                        column: x => x.GameSessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreTransactions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameCategories_Key",
                table: "GameCategories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameCategories_PackageId",
                table: "GameCategories",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePackages_Key",
                table: "GamePackages",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestionEligiblePlayers_PlayerId",
                table: "GameQuestionEligiblePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestionEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "GameQuestionEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestionInstances_QuestionId",
                table: "GameQuestionInstances",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_GameQuestionInstances_RoundId",
                table: "GameQuestionInstances",
                column: "RoundId");

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
                name: "IX_GameRounds_CategoryId",
                table: "GameRounds",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GameRounds_GameSessionId",
                table: "GameRounds",
                column: "GameSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_RoomId",
                table: "GameSessions",
                column: "RoomId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSelectionAnswers_QuestionInstanceId_VoterPlayerId",
                table: "PlayerSelectionAnswers",
                columns: new[] { "QuestionInstanceId", "VoterPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSelectionAnswers_SelectedPlayerId",
                table: "PlayerSelectionAnswers",
                column: "SelectedPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSelectionAnswers_VoterPlayerId",
                table: "PlayerSelectionAnswers",
                column: "VoterPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreTransactions_GameSessionId",
                table: "ScoreTransactions",
                column: "GameSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreTransactions_PlayerId",
                table: "ScoreTransactions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreTransactions_QuestionInstanceId_PlayerId",
                table: "ScoreTransactions",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameQuestionEligiblePlayers");

            migrationBuilder.DropTable(
                name: "PlayerSelectionAnswers");

            migrationBuilder.DropTable(
                name: "ScoreTransactions");

            migrationBuilder.DropTable(
                name: "GameQuestionInstances");

            migrationBuilder.DropTable(
                name: "GameQuestions");

            migrationBuilder.DropTable(
                name: "GameRounds");

            migrationBuilder.DropTable(
                name: "GameCategories");

            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "GamePackages");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SelectedPackageKeys",
                table: "GameRooms");
        }
    }
}
