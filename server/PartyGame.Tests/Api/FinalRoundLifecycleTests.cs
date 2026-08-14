using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class FinalRoundLifecycleTests
{
    [Fact]
    public async Task EnabledFinalRound_EntersExtraRoundWithStablePerPlayerPromptsBeforeCompletion()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: 3, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.Include(session => session.Room).ThenInclude(room => room.Settings).SingleAsync(session => session.Id == room.GameSessionId);
            session.Room.Settings.FinalRoundEnabled = true;
            session.TotalRounds = 2;
            await db.SaveChangesAsync();
        }

        Assert.Equal(GameStage.CollectingFinalSelfies, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        await using var verification = harness.Factory.Services.CreateAsyncScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var finalSession = await verifyDb.GameSessions.AsNoTracking().SingleAsync(session => session.Id == room.GameSessionId);
        var final = FinalRoundState.Read(finalSession.FinalRoundStateJson)!;
        Assert.Equal(2, finalSession.CurrentRoundNumber);
        Assert.Equal(3, final.Artifacts.Count);
        Assert.Equal(2, final.TotalPasses);
        Assert.Equal(3, final.Artifacts.Select(artifact => artifact.SubjectPlayerId).Distinct().Count());
        Assert.All(final.Artifacts, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.TargetRolePl)));
    }

    [Fact]
    public async Task DisabledFinalRound_PreservesLegacyGameSummaryTransition()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(GameStage.GameSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task FinalRound_CirculationHasOneDistinctNonOwnerEditorPerPass(int playerCount)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: playerCount, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.Include(session => session.Room).ThenInclude(room => room.Settings).SingleAsync(session => session.Id == room.GameSessionId);
            session.Room.Settings.FinalRoundEnabled = true;
            await db.SaveChangesAsync();
        }

        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow);
        await using var verification = harness.Factory.Services.CreateAsyncScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var final = FinalRoundState.Read((await verifyDb.GameSessions.AsNoTracking().SingleAsync(session => session.Id == room.GameSessionId)).FinalRoundStateJson)!;
        var players = final.Artifacts.Select(artifact => artifact.SubjectPlayerId).OrderBy(id => id).ToList();
        Assert.Equal(playerCount - 1, final.TotalPasses);
        foreach (var artifact in final.Artifacts)
        {
            var subject = players.IndexOf(artifact.SubjectPlayerId);
            var editors = Enumerable.Range(1, final.TotalPasses).Select(pass => players[(subject + pass) % players.Count]).ToList();
            Assert.DoesNotContain(artifact.SubjectPlayerId, editors);
            Assert.Equal(final.TotalPasses, editors.Distinct().Count());
        }
    }
}
