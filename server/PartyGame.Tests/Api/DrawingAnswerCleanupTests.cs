using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerCleanupTests
{
    [Fact]
    public async Task DatabaseCommitFailure_RemovesRowsAndBothFinalPngFiles()
    {
        var interceptor = new ArmedFailureInterceptor();
        await using var harness = new PhotoAnswerTestHarness(configureServices: services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContext<PartyGameDbContext>((provider, options) => options.AddInterceptors(provider.GetRequiredService<ArmedFailureInterceptor>()));
        });
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        interceptor.Armed = true;
        var response = await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync());
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("drawing_answer_storage_failed", await response.Content.ReadAsStringAsync());
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((0, 0, 0), (counts.Submissions, counts.Assets, harness.FinalPngCount()));
    }

    private sealed class ArmedFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context?.ChangeTracker.Entries<DrawingAnswerSubmission>().Any(entry => entry.State == EntityState.Added) == true) throw new DbUpdateException("Injected drawing commit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
