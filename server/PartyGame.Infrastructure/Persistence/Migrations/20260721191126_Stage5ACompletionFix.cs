using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage5ACompletionFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DrawingAnswerEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingAnswerEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerEligiblePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawingAnswerSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientSubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevealOrder = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingAnswerSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerSubmissions_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerSubmissions_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerSubmissions_Players_AuthorPlayerId",
                        column: x => x.AuthorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawingAnswerVoteEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingAnswerVoteEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerVoteEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerVoteEligiblePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawingAnswerVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoterPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedDrawingAnswerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingAnswerVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerVotes_DrawingAnswerSubmissions_SelectedDrawingAnswerId",
                        column: x => x.SelectedDrawingAnswerId,
                        principalTable: "DrawingAnswerSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerVotes_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingAnswerVotes_Players_VoterPlayerId",
                        column: x => x.VoterPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerEligiblePlayers_PlayerId",
                table: "DrawingAnswerEligiblePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "DrawingAnswerEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerSubmissions_AuthorPlayerId",
                table: "DrawingAnswerSubmissions",
                column: "AuthorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerSubmissions_MediaAssetId",
                table: "DrawingAnswerSubmissions",
                column: "MediaAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerSubmissions_QuestionInstanceId_AuthorPlayerId",
                table: "DrawingAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "AuthorPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerSubmissions_QuestionInstanceId_ClientSubmissionId",
                table: "DrawingAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "ClientSubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerSubmissions_QuestionInstanceId_RevealOrder",
                table: "DrawingAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "RevealOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerVoteEligiblePlayers_PlayerId",
                table: "DrawingAnswerVoteEligiblePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerVoteEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "DrawingAnswerVoteEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerVotes_QuestionInstanceId_VoterPlayerId",
                table: "DrawingAnswerVotes",
                columns: new[] { "QuestionInstanceId", "VoterPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerVotes_SelectedDrawingAnswerId",
                table: "DrawingAnswerVotes",
                column: "SelectedDrawingAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAnswerVotes_VoterPlayerId",
                table: "DrawingAnswerVotes",
                column: "VoterPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawingAnswerEligiblePlayers");

            migrationBuilder.DropTable(
                name: "DrawingAnswerVoteEligiblePlayers");

            migrationBuilder.DropTable(
                name: "DrawingAnswerVotes");

            migrationBuilder.DropTable(
                name: "DrawingAnswerSubmissions");
        }
    }
}
