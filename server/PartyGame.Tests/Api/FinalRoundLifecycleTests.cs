using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

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

    [Fact]
    public async Task FinalSelfieDeadline_WithNoPersistedSelfies_SkipsDirectlyToPresentation()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: 3, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await using (var setup = harness.Factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.Include(candidate => candidate.Room).ThenInclude(candidate => candidate.Settings).SingleAsync(candidate => candidate.Id == room.GameSessionId);
            session.Room.Settings.FinalRoundEnabled = true;
            await db.SaveChangesAsync();
        }

        Assert.Equal(GameStage.CollectingFinalSelfies, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        DateTimeOffset firstDeadline;
        await using (var beforeTimeout = harness.Factory.Services.CreateAsyncScope())
        {
            firstDeadline = (await beforeTimeout.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions
                .Where(candidate => candidate.Id == room.GameSessionId).Select(candidate => candidate.StageEndsAtUtc).SingleAsync())!.Value;
        }

        Assert.Equal(GameStage.ShowingFinalPresentation, await harness.ProcessAtAsync(room, firstDeadline.AddSeconds(1)));
        await using var verification = harness.Factory.Services.CreateAsyncScope();
        var renewed = await verification.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions
            .AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
        Assert.Equal(GameStage.ShowingFinalPresentation, renewed.Stage);
        Assert.True(renewed.StageEndsAtUtc > firstDeadline);
        Assert.Empty(FinalRoundState.Read(renewed.FinalRoundStateJson)!.Artifacts);
    }

    [Fact]
    public async Task FinalSelfieDeadline_DropsMissingDisconnectedOwnerAndAdvances()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: 3, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await using (var setup = harness.Factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.Include(candidate => candidate.Room).ThenInclude(candidate => candidate.Settings).SingleAsync(candidate => candidate.Id == room.GameSessionId);
            session.Room.Settings.FinalRoundEnabled = true;
            await db.SaveChangesAsync();
        }

        Assert.Equal(GameStage.CollectingFinalSelfies, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        await using (var seeded = harness.Factory.Services.CreateAsyncScope())
        {
            var services = seeded.ServiceProvider;
            await services.GetRequiredService<IRoomService>().DisconnectPlayerAsync(room.RoomCode, room.Players[2].PlayerId);
            var db = services.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.SingleAsync(candidate => candidate.Id == room.GameSessionId);
            var final = FinalRoundState.Read(session.FinalRoundStateJson)!;
            foreach (var artifact in final.Artifacts.Take(2))
            {
                artifact.OriginalMediaAssetId = Guid.NewGuid();
                artifact.FinalMediaAssetId = artifact.OriginalMediaAssetId;
            }
            session.FinalRoundStateJson = final.Write();
            await db.SaveChangesAsync();
        }

        Assert.Equal(GameStage.CollectingFinalEdits, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));
        await using var verification = harness.Factory.Services.CreateAsyncScope();
        var state = FinalRoundState.Read((await verification.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId)).FinalRoundStateJson)!;
        Assert.Equal(2, state.Artifacts.Count);
        Assert.Equal(1, state.TotalPasses);
    }

    [Fact]
    public async Task FinalSelfieDeadline_ReconnectedBeforeDeadlineRetainsEveryParticipant()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await CreateFinalSelfieRoomAsync(harness);
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            var player = room.Players[2];
            await service.DisconnectPlayerAsync(room.RoomCode, player.PlayerId);
            await service.AttachPlayerAsync(room.RoomCode, player.PlayerId, player.ReconnectToken);
        }
        await MarkSelfiesPersistedAsync(harness, room, 3);

        Assert.Equal(GameStage.CollectingFinalEdits, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));
        var state = await ReadFinalStateAsync(harness, room);
        Assert.Equal(3, state.Artifacts.Count);
        Assert.Equal(2, state.TotalPasses);
    }

    [Fact]
    public async Task FinalSelfieDeadline_PreservesSubmittedSelfieAfterItsOwnerDisconnects()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await CreateFinalSelfieRoomAsync(harness);
        await MarkSelfiesPersistedAsync(harness, room, 1);
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            await service.DisconnectPlayerAsync(room.RoomCode, room.Players[0].PlayerId);
            await service.DisconnectPlayerAsync(room.RoomCode, room.Players[1].PlayerId);
            await service.DisconnectPlayerAsync(room.RoomCode, room.Players[2].PlayerId);
        }

        Assert.Equal(GameStage.ShowingFinalPresentation, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));
        var state = await ReadFinalStateAsync(harness, room);
        var artifact = Assert.Single(state.Artifacts);
        Assert.Equal(room.Players[0].PlayerId, artifact.SubjectPlayerId);
        Assert.NotNull(artifact.OriginalMediaAssetId);
    }

    [Fact]
    public async Task FinalSelfieDeadline_DuplicateTimerDeliveryDoesNotAdvanceTwice()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await CreateFinalSelfieRoomAsync(harness);
        await MarkSelfiesPersistedAsync(harness, room, 2);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.Equal(GameStage.CollectingFinalEdits, await harness.ProcessAtAsync(room, deadline));
        Assert.Equal(GameStage.CollectingFinalEdits, await harness.ProcessAtAsync(room, deadline));
        var state = await ReadFinalStateAsync(harness, room);
        Assert.Equal(1, state.CurrentPass);
        Assert.Equal(2, state.Artifacts.Count);
    }

    private static async Task<PhotoRoomAccess> CreateFinalSelfieRoomAsync(PhotoAnswerTestHarness harness)
    {
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: 3, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var session = await db.GameSessions.Include(candidate => candidate.Room).ThenInclude(candidate => candidate.Settings).SingleAsync(candidate => candidate.Id == room.GameSessionId);
        session.Room.Settings.FinalRoundEnabled = true;
        await db.SaveChangesAsync();
        Assert.Equal(GameStage.CollectingFinalSelfies, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        return room;
    }

    private static async Task MarkSelfiesPersistedAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room, int count)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var session = await db.GameSessions.SingleAsync(candidate => candidate.Id == room.GameSessionId);
        var state = FinalRoundState.Read(session.FinalRoundStateJson)!;
        foreach (var artifact in state.Artifacts.Take(count))
        {
            artifact.OriginalMediaAssetId = Guid.NewGuid();
            artifact.FinalMediaAssetId = artifact.OriginalMediaAssetId;
        }
        session.FinalRoundStateJson = state.Write();
        await db.SaveChangesAsync();
    }

    private static async Task<FinalRoundState> ReadFinalStateAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
        return FinalRoundState.Read(session.FinalRoundStateJson)!;
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
