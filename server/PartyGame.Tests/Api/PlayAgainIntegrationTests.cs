using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class PlayAgainIntegrationTests
{
    [Fact]
    public async Task HostCanResetCompletedGame_WhileKeepingRoomAndPlayersForAnotherReadyCycle()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var access = await harness.CreateRoomAsync(GameStage.Completed, QuestionType.PlayerSelection);

        await using (var setup = harness.Factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var room = await db.GameRooms.Include(candidate => candidate.Players).Include(candidate => candidate.Session)
                .SingleAsync(candidate => candidate.Id == access.RoomId);
            room.Phase = RoomPhase.Completed;
            room.Session!.Stage = GameStage.Completed;
            room.Session.FinalRoundStateJson = "{\"completed\":true}";
            foreach (var player in room.Players)
            {
                player.Score = 250;
                player.IsReady = true;
                player.HasProfilePhoto = true;
            }
            db.MediaAssets.Add(new MediaAsset
            {
                Id = Guid.NewGuid(), MediaKind = MediaKind.PhotoAnswer, RoomId = room.Id, PlayerId = room.Players[0].Id,
                QuestionInstanceId = access.QuestionInstanceId, DisplayStorageKey = "game/display.jpg", ThumbnailStorageKey = "game/thumb.jpg",
                ContentType = "image/jpeg", Width = 1, Height = 1, ByteLength = 1, Sha256 = new string('a', 64), CreatedAtUtc = DateTimeOffset.UtcNow
            });
            db.SubmissionReceipts.Add(new SubmissionReceipt
            {
                Id = Guid.NewGuid(), RoomId = room.Id, PlayerId = room.Players[0].Id, QuestionInstanceId = access.QuestionInstanceId,
                ActionType = SubmissionActionType.PlayerSelection, ClientSubmissionId = Guid.NewGuid(), PayloadFingerprint = new string('b', 64), CreatedAtUtc = DateTimeOffset.UtcNow
            });
            db.SubmissionAuditEntries.Add(new SubmissionAuditEntry
            {
                Id = Guid.NewGuid(), RoomId = room.Id, PlayerId = room.Players[0].Id, QuestionInstanceId = access.QuestionInstanceId,
                ActionType = SubmissionActionType.PlayerSelection, ClientSubmissionId = Guid.NewGuid(), PayloadFingerprint = new string('b', 64), Result = SubmissionAuditResult.Accepted, CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var action = harness.Factory.Services.CreateAsyncScope())
        {
            var rooms = action.ServiceProvider.GetRequiredService<IRoomService>();
            var reset = await rooms.PlayAgainAsync(access.RoomCode, access.Players[0].PlayerId, access.Players[0].Token);
            Assert.True(reset.PublicStateChanged);
            Assert.Equal(RoomPhase.Lobby, reset.Room.Phase);
            Assert.Null(reset.Room.Session);
            Assert.False((await rooms.PlayAgainAsync(access.RoomCode, access.Players[0].PlayerId, access.Players[0].Token)).PublicStateChanged);
            await Assert.ThrowsAsync<RoomConflictException>(() => rooms.PlayAgainAsync(access.RoomCode, access.Players[1].PlayerId, access.Players[1].Token));
        }

        await using (var assertion = harness.Factory.Services.CreateAsyncScope())
        {
            var db = assertion.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var room = await db.GameRooms.AsNoTracking().Include(candidate => candidate.Players).SingleAsync(candidate => candidate.Id == access.RoomId);
            Assert.Equal(RoomPhase.Lobby, room.Phase);
            Assert.Null(room.StartedAtUtc);
            Assert.Equal(access.Players.Select(player => player.PlayerId).Order(), room.Players.Select(player => player.Id).Order());
            Assert.All(room.Players, player => { Assert.False(player.IsReady); Assert.Equal(0, player.Score); });
            Assert.Equal(0, await db.GameSessions.CountAsync());
            Assert.Equal(0, await db.MediaAssets.CountAsync(asset => asset.RoomId == access.RoomId));
            Assert.Equal(0, await db.SubmissionReceipts.CountAsync(entry => entry.RoomId == access.RoomId));
            Assert.Equal(0, await db.SubmissionAuditEntries.CountAsync(entry => entry.RoomId == access.RoomId));
        }

        await using (var nextCycle = harness.Factory.Services.CreateAsyncScope())
        {
            var rooms = nextCycle.ServiceProvider.GetRequiredService<IRoomService>();
            RoomMutationResult? finalReady = null;
            foreach (var player in access.Players)
                finalReady = await rooms.SetReadyAsync(access.RoomCode, player.PlayerId, player.Token, true);
            Assert.True(finalReady!.StartedNow);
        }
    }
}
