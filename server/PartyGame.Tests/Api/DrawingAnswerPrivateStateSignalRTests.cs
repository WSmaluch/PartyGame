using Microsoft.AspNetCore.SignalR.Client;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerPrivateStateSignalRTests
{
    [Fact]
    public async Task UploadPrivateStateEvent_GoesOnlyToSubmittingPlayer()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        await using var playerA = DrawingAnswerGameE2ETests.Connection(harness); await using var playerB = DrawingAnswerGameE2ETests.Connection(harness); await using var display = DrawingAnswerGameE2ETests.Connection(harness);
        var receivedA = new TaskCompletionSource<PlayerPrivateGameState>(TaskCreationOptions.RunContinuationsAsynchronously); var receivedB = new TaskCompletionSource<PlayerPrivateGameState>(TaskCreationOptions.RunContinuationsAsynchronously); var receivedDisplay = new TaskCompletionSource<PlayerPrivateGameState>(TaskCreationOptions.RunContinuationsAsynchronously);
        playerA.On<PlayerPrivateGameState>("PlayerPrivateGameStateUpdated", state => { if (state.HasSubmittedDrawingAnswer) receivedA.TrySetResult(state); }); playerB.On<PlayerPrivateGameState>("PlayerPrivateGameStateUpdated", state => { if (state.HasSubmittedDrawingAnswer) receivedB.TrySetResult(state); }); display.On<PlayerPrivateGameState>("PlayerPrivateGameStateUpdated", state => { if (state.HasSubmittedDrawingAnswer) receivedDisplay.TrySetResult(state); });
        await Task.WhenAll(playerA.StartAsync(), playerB.StartAsync(), display.StartAsync()); await display.InvokeAsync("AttachDisplay", room.RoomCode); await playerA.InvokeAsync("AttachPlayer", room.RoomCode, room.Players[0].PlayerId, room.Players[0].Token); await playerB.InvokeAsync("AttachPlayer", room.RoomCode, room.Players[1].PlayerId, room.Players[1].Token);
        Assert.True((await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).IsSuccessStatusCode);
        var state = await receivedA.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(state.HasSubmittedDrawingAnswer); Assert.NotNull(state.OwnDrawingAnswerId);
        await Task.Delay(150); Assert.False(receivedB.Task.IsCompleted); Assert.False(receivedDisplay.Task.IsCompleted);
    }
}
