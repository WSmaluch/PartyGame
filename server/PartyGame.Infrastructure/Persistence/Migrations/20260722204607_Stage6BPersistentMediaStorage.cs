using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage6BPersistentMediaStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProfilePhotoMediaAssetId",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaKind",
                table: "MediaAssets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "MediaAssets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionInstanceId",
                table: "MediaAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "MediaAssets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing 6A assets belong exclusively to PhotoAnswer or DrawingAnswer.
            // Populate ownership before adding required foreign keys so an upgraded
            // baseline database retains all historical game media.
            migrationBuilder.Sql("""
                UPDATE MediaAssets
                SET MediaKind = 1,
                    PlayerId = (SELECT AuthorPlayerId FROM PhotoAnswerSubmissions WHERE MediaAssetId = MediaAssets.Id),
                    QuestionInstanceId = (SELECT QuestionInstanceId FROM PhotoAnswerSubmissions WHERE MediaAssetId = MediaAssets.Id),
                    RoomId = (
                        SELECT GameSessions.RoomId
                        FROM PhotoAnswerSubmissions
                        INNER JOIN GameQuestionInstances ON GameQuestionInstances.Id = PhotoAnswerSubmissions.QuestionInstanceId
                        INNER JOIN GameRounds ON GameRounds.Id = GameQuestionInstances.RoundId
                        INNER JOIN GameSessions ON GameSessions.Id = GameRounds.GameSessionId
                        WHERE PhotoAnswerSubmissions.MediaAssetId = MediaAssets.Id)
                WHERE EXISTS (SELECT 1 FROM PhotoAnswerSubmissions WHERE PhotoAnswerSubmissions.MediaAssetId = MediaAssets.Id);
                """);

            migrationBuilder.Sql("""
                UPDATE MediaAssets
                SET MediaKind = 2,
                    PlayerId = (SELECT AuthorPlayerId FROM DrawingAnswerSubmissions WHERE MediaAssetId = MediaAssets.Id),
                    QuestionInstanceId = (SELECT QuestionInstanceId FROM DrawingAnswerSubmissions WHERE MediaAssetId = MediaAssets.Id),
                    RoomId = (
                        SELECT GameSessions.RoomId
                        FROM DrawingAnswerSubmissions
                        INNER JOIN GameQuestionInstances ON GameQuestionInstances.Id = DrawingAnswerSubmissions.QuestionInstanceId
                        INNER JOIN GameRounds ON GameRounds.Id = GameQuestionInstances.RoundId
                        INNER JOIN GameSessions ON GameSessions.Id = GameRounds.GameSessionId
                        WHERE DrawingAnswerSubmissions.MediaAssetId = MediaAssets.Id)
                WHERE EXISTS (SELECT 1 FROM DrawingAnswerSubmissions WHERE DrawingAnswerSubmissions.MediaAssetId = MediaAssets.Id);
                """);

            migrationBuilder.Sql("UPDATE MediaAssets SET StorageProvider = 'LocalFileSystem' WHERE StorageProvider = 'Local';");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ProfilePhotoMediaAssetId",
                table: "Players",
                column: "ProfilePhotoMediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_DisplayStorageKey",
                table: "MediaAssets",
                column: "DisplayStorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PlayerId",
                table: "MediaAssets",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_QuestionInstanceId",
                table: "MediaAssets",
                column: "QuestionInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_RoomId",
                table: "MediaAssets",
                column: "RoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaAssets_GameQuestionInstances_QuestionInstanceId",
                table: "MediaAssets",
                column: "QuestionInstanceId",
                principalTable: "GameQuestionInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaAssets_GameRooms_RoomId",
                table: "MediaAssets",
                column: "RoomId",
                principalTable: "GameRooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaAssets_Players_PlayerId",
                table: "MediaAssets",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_MediaAssets_ProfilePhotoMediaAssetId",
                table: "Players",
                column: "ProfilePhotoMediaAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_GameQuestionInstances_QuestionInstanceId",
                table: "MediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_GameRooms_RoomId",
                table: "MediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_Players_PlayerId",
                table: "MediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_MediaAssets_ProfilePhotoMediaAssetId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_ProfilePhotoMediaAssetId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_DisplayStorageKey",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_PlayerId",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_QuestionInstanceId",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_RoomId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoMediaAssetId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "MediaKind",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "QuestionInstanceId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "MediaAssets");
        }
    }
}
