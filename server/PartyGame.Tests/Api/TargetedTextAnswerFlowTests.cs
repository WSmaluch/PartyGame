using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class TargetedTextAnswerFlowTests
{
    [Fact]
    public async Task PersistedTarget_RendersPrompt_AndAllEligibleAnswersAdvanceToVotingWithoutDuplicates()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(
            GameStage.CollectingTextAnswers,
            QuestionType.TextAnswer,
            playerCount: 3,
            eligibleCount: 0);

        Guid targetId;
        List<PhotoPlayerAccess> eligiblePlayers;
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var instance = await db.GameQuestionInstances
                .AsNoTracking()
                .SingleAsync(instance => instance.Id == room.QuestionInstanceId);
            var players = (await db.Players.AsNoTracking().Where(player => player.RoomId == room.RoomId).ToListAsync())
                .OrderBy(player => player.Nickname, StringComparer.Ordinal)
                .ToList();

            targetId = players[0].Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE GameQuestionInstances
                SET SubjectPlayerId = {targetId}
                WHERE Id = {instance.Id}
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE GameQuestions
                SET TextPl = {"Co {player} na pewno zapomni spakować?"},
                    TextEn = {"What will {player} definitely forget to pack?"}
                WHERE Id = {instance.QuestionId}
                """);
            foreach (var player in players.Skip(1))
            {
                db.TextAnswerEligiblePlayers.Add(new TextAnswerEligiblePlayer
                {
                    Id = Guid.NewGuid(),
                    QuestionInstanceId = instance.Id,
                    PlayerId = player.Id
                });
            }
            await db.SaveChangesAsync();
            eligiblePlayers = room.Players.Where(player => player.PlayerId != targetId).ToList();
        }

        var collectingJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        using (var collecting = JsonDocument.Parse(collectingJson))
        {
            var prompt = collecting.RootElement.GetProperty("game").GetProperty("question").GetProperty("text").GetProperty("pl").GetString();
            Assert.Equal("Co Player 1 na pewno zapomni spakować?", prompt);
            Assert.DoesNotContain("{player}", prompt, StringComparison.OrdinalIgnoreCase);
        }

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var rooms = scope.ServiceProvider.GetRequiredService<IRoomService>();
            foreach (var player in eligiblePlayers)
            {
                await rooms.SubmitTextAnswerAsync(room.RoomCode, player.PlayerId, player.Token, $"Odpowiedź {player.Nickname}", room.QuestionInstanceId, Guid.NewGuid());
            }
            // A replay must not create another answer or alter the target's flow.
            await rooms.SubmitTextAnswerAsync(room.RoomCode, eligiblePlayers[0].PlayerId, eligiblePlayers[0].Token, "duplikat", room.QuestionInstanceId, Guid.NewGuid());
        }

        Assert.Equal(GameStage.RevealingTextAnswers, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        Assert.Equal(GameStage.CollectingTextAnswerVotes, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var instance = await db.GameQuestionInstances
                .Include(candidate => candidate.TextAnswerSubmissions)
                .Include(candidate => candidate.TextAnswerVoteEligiblePlayers)
                .SingleAsync(candidate => candidate.Id == room.QuestionInstanceId);
            Assert.Equal(2, instance.TextAnswerSubmissions.Count);
            Assert.Equal(3, instance.TextAnswerVoteEligiblePlayers.Count);

            var snapshot = (await scope.ServiceProvider.GetRequiredService<IRoomService>().GetAsync(room.RoomCode)).ToSnapshot();
            Assert.Equal(2, snapshot.Game!.TextResults!.VotingOptions!.Count);

            var rooms = scope.ServiceProvider.GetRequiredService<IRoomService>();
            foreach (var player in room.Players)
            {
                var selected = instance.TextAnswerSubmissions.First(answer => answer.AuthorPlayerId != player.PlayerId);
                await rooms.SubmitTextAnswerVoteAsync(room.RoomCode, player.PlayerId, player.Token, selected.Id, room.QuestionInstanceId, Guid.NewGuid());
            }
        }

        await using var verificationScope = harness.Factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        Assert.Equal(GameStage.ShowingTextAnswerResults, await verificationDb.GameSessions
            .Where(session => session.Id == room.GameSessionId)
            .Select(session => session.Stage)
            .SingleAsync());
        Assert.Equal(2, await verificationDb.TextAnswerSubmissions.CountAsync(answer => answer.QuestionInstanceId == room.QuestionInstanceId));
        Assert.Equal(3, await verificationDb.TextAnswerVotes.CountAsync(vote => vote.QuestionInstanceId == room.QuestionInstanceId));
    }
}
