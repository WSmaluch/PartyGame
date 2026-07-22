using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4APhotoAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayStorageKey = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ThumbnailStorageKey = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnswerEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnswerEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerEligiblePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnswerVoteEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnswerVoteEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerVoteEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerVoteEligiblePlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnswerSubmissions",
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
                    table.PrimaryKey("PK_PhotoAnswerSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerSubmissions_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerSubmissions_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerSubmissions_Players_AuthorPlayerId",
                        column: x => x.AuthorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnswerVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoterPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedPhotoAnswerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnswerVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerVotes_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerVotes_PhotoAnswerSubmissions_SelectedPhotoAnswerId",
                        column: x => x.SelectedPhotoAnswerId,
                        principalTable: "PhotoAnswerSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnswerVotes_Players_VoterPlayerId",
                        column: x => x.VoterPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerEligiblePlayers_PlayerId",
                table: "PhotoAnswerEligiblePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "PhotoAnswerEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerSubmissions_AuthorPlayerId",
                table: "PhotoAnswerSubmissions",
                column: "AuthorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerSubmissions_MediaAssetId",
                table: "PhotoAnswerSubmissions",
                column: "MediaAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerSubmissions_QuestionInstanceId_AuthorPlayerId",
                table: "PhotoAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "AuthorPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerSubmissions_QuestionInstanceId_ClientSubmissionId",
                table: "PhotoAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "ClientSubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerSubmissions_QuestionInstanceId_RevealOrder",
                table: "PhotoAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "RevealOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerVoteEligiblePlayers_PlayerId",
                table: "PhotoAnswerVoteEligiblePlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerVoteEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "PhotoAnswerVoteEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerVotes_QuestionInstanceId_VoterPlayerId",
                table: "PhotoAnswerVotes",
                columns: new[] { "QuestionInstanceId", "VoterPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerVotes_SelectedPhotoAnswerId",
                table: "PhotoAnswerVotes",
                column: "SelectedPhotoAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnswerVotes_VoterPlayerId",
                table: "PhotoAnswerVotes",
                column: "VoterPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoAnswerEligiblePlayers");

            migrationBuilder.DropTable(
                name: "PhotoAnswerVoteEligiblePlayers");

            migrationBuilder.DropTable(
                name: "PhotoAnswerVotes");

            migrationBuilder.DropTable(
                name: "PhotoAnswerSubmissions");

            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}
