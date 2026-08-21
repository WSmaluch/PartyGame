using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class PlayerSelectionSelfVoteIntegrationTests
{
    [Fact]
    public async Task EligiblePlayer_CanVoteForSelf_AndReplayRemainsIdempotent()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingPlayerSelections, QuestionType.PlayerSelection);
        var voter = room.Players[0];

        await using (var setupScope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            foreach (var player in room.Players)
            {
                db.GameQuestionEligiblePlayers.Add(new GameQuestionEligiblePlayer
                {
                    Id = Guid.NewGuid(),
                    QuestionInstanceId = room.QuestionInstanceId,
                    PlayerId = player.PlayerId
                });
            }
            await db.SaveChangesAsync();
        }

        var submissionId = Guid.NewGuid();
        await using (var actionScope = harness.Factory.Services.CreateAsyncScope())
        {
            var roomService = actionScope.ServiceProvider.GetRequiredService<IRoomService>();
            var accepted = await roomService.SubmitSelectionAsync(
                room.RoomCode, voter.PlayerId, voter.Token, voter.PlayerId, room.QuestionInstanceId, submissionId);
            var replay = await roomService.SubmitSelectionAsync(
                room.RoomCode, voter.PlayerId, voter.Token, voter.PlayerId, room.QuestionInstanceId, submissionId);

            Assert.True(accepted.PublicStateChanged);
            Assert.False(replay.PublicStateChanged);
        }

        await using var assertionScope = harness.Factory.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var vote = await assertionDb.PlayerSelectionAnswers.SingleAsync(answer => answer.QuestionInstanceId == room.QuestionInstanceId);
        Assert.Equal(voter.PlayerId, vote.VoterPlayerId);
        Assert.Equal(voter.PlayerId, vote.SelectedPlayerId);
    }
}
