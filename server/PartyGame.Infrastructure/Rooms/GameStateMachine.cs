using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Rooms;

public sealed class GameStateMachine(PartyGameDbContext dbContext, ScoreCalculator scoreCalculator, Microsoft.Extensions.Options.IOptions<GameFlowOptions> options, IRandomProvider randomProvider)
{
    private static readonly TimeSpan CategoryIntroDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuestionIntroDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CollectingPlayerSelectionsDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShowingQuestionResultsDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RoundSummaryDuration = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan GameSummaryDuration = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CollectingTextAnswersDuration = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan CollectingTextAnswerVotesDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShowingTextAnswerResultsDuration = TimeSpan.FromSeconds(8);

    public async Task<bool> ProcessTransitionAsync(Guid gameSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await dbContext.GameSessions
            .Include(s => s.Room)
                .ThenInclude(r => r.Players)
            .Include(s => s.Rounds)
                .ThenInclude(r => r.Questions)
                    .ThenInclude(q => q.Question)
            .FirstOrDefaultAsync(s => s.Id == gameSessionId, cancellationToken);

        if (session == null || session.Stage == GameStage.PausedForDisplay || session.Stage == GameStage.Completed)
        {
            return false; // Nothing to transition
        }

        if (session.StageEndsAtUtc == null || session.StageEndsAtUtc > now)
        {
            return false; // Time not up yet
        }

        bool changed = false;

        switch (session.Stage)
        {
            case GameStage.CategoryIntro:
                TransitionToQuestionIntro(session, now);
                changed = true;
                break;
            case GameStage.QuestionIntro:
                var currentInstance = GetCurrentQuestionInstance(session);
                if (currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.TextAnswer)
                {
                    TransitionToCollectingTextAnswers(session, now);
                }
                else if (currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.PhotoAnswer)
                {
                    TransitionToCollectingPhotoAnswers(session, now);
                }
                else if (currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.DrawingAnswer)
                {
                    TransitionToCollectingDrawingAnswers(session, now);
                }
                else
                {
                    TransitionToCollectingPlayerSelections(session, now);
                }
                changed = true;
                break;
            case GameStage.CollectingPlayerSelections:
                await scoreCalculator.CalculateAndApplyScoresAsync(session, now, cancellationToken);
                TransitionToShowingQuestionResults(session, now);
                changed = true;
                break;
            case GameStage.ShowingQuestionResults:
                changed = TransitionFromResults(session, now);
                break;
            case GameStage.CollectingTextAnswers:
                var currentInstanceTA = GetCurrentQuestionInstance(session);
                var answersCount = dbContext.TextAnswerSubmissions.Count(s => s.QuestionInstanceId == currentInstanceTA.Id);
                if (answersCount == 0)
                    TransitionToShowingTextAnswerResults(session, now);
                else
                    TransitionToRevealingTextAnswers(session, now);
                changed = true;
                break;
            case GameStage.RevealingTextAnswers:
                TransitionToCollectingTextAnswerVotes(session, now);
                changed = true;
                break;
            case GameStage.CollectingTextAnswerVotes:
                await scoreCalculator.CalculateAndApplyTextAnswerScoresAsync(session, now, cancellationToken);
                TransitionToShowingTextAnswerResults(session, now);
                changed = true;
                break;
            case GameStage.ShowingTextAnswerResults:
                changed = TransitionFromResults(session, now);
                break;
            case GameStage.CollectingPhotoAnswers:
                TransitionFromCollectingPhotoAnswers(session, now);
                changed = true;
                break;
            case GameStage.RevealingPhotoAnswers:
                TransitionToCollectingPhotoAnswerVotes(session, now);
                changed = true;
                break;
            case GameStage.CollectingPhotoAnswerVotes:
                await scoreCalculator.CalculateAndApplyPhotoAnswerScoresAsync(session, now, cancellationToken);
                TransitionToShowingPhotoAnswerResults(session, now);
                changed = true;
                break;
            case GameStage.ShowingPhotoAnswerResults:
                changed = TransitionFromResults(session, now);
                break;
            case GameStage.CollectingDrawingAnswers:
                TransitionFromCollectingDrawingAnswers(session, now); changed = true; break;
            case GameStage.RevealingDrawingAnswers:
                TransitionToCollectingDrawingAnswerVotes(session, now); changed = true; break;
            case GameStage.CollectingDrawingAnswerVotes:
                await scoreCalculator.CalculateAndApplyDrawingAnswerScoresAsync(session, now, cancellationToken);
                TransitionToShowingDrawingAnswerResults(session, now); changed = true; break;
            case GameStage.ShowingDrawingAnswerResults:
                changed = TransitionFromResults(session, now); break;
            case GameStage.RoundSummary:
                changed = TransitionFromRoundSummary(session, now);
                break;
            case GameStage.GameSummary:
                TransitionToCompleted(session, now);
                changed = true;
                break;
        }

        return changed;
    }

    public void TransitionToNextStageImmediate(GameSession session, DateTimeOffset now)
    {
        // For early termination (legacy/timeout adjustment)
        session.StageEndsAtUtc = now;
    }

    public async Task<bool> ForceTransitionAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.Stage == GameStage.CollectingPlayerSelections)
        {
            await scoreCalculator.CalculateAndApplyScoresAsync(session, now, cancellationToken);
            TransitionToShowingQuestionResults(session, now);
            return true;
        }
        else if (session.Stage == GameStage.CollectingTextAnswers)
        {
            var currentInstanceTA = GetCurrentQuestionInstance(session);
            var answersCount = dbContext.TextAnswerSubmissions.Count(s => s.QuestionInstanceId == currentInstanceTA.Id);
            if (answersCount == 0)
                TransitionToShowingTextAnswerResults(session, now);
            else
                TransitionToRevealingTextAnswers(session, now);
            return true;
        }
        else if (session.Stage == GameStage.CollectingTextAnswerVotes)
        {
            await scoreCalculator.CalculateAndApplyTextAnswerScoresAsync(session, now, cancellationToken);
            TransitionToShowingTextAnswerResults(session, now);
            return true;
        }
        else if (session.Stage == GameStage.CollectingPhotoAnswers)
        {
            TransitionFromCollectingPhotoAnswers(session, now);
            return true;
        }
        else if (session.Stage == GameStage.CollectingPhotoAnswerVotes)
        {
            await scoreCalculator.CalculateAndApplyPhotoAnswerScoresAsync(session, now, cancellationToken);
            TransitionToShowingPhotoAnswerResults(session, now);
            return true;
        }
        else if (session.Stage == GameStage.CollectingDrawingAnswers) { TransitionFromCollectingDrawingAnswers(session, now); return true; }
        else if (session.Stage == GameStage.CollectingDrawingAnswerVotes) { await scoreCalculator.CalculateAndApplyDrawingAnswerScoresAsync(session, now, cancellationToken); TransitionToShowingDrawingAnswerResults(session, now); return true; }

        return false;
    }

    private void TransitionToQuestionIntro(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.QuestionIntro;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(QuestionIntroDuration);

        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.Stage = GameStage.QuestionIntro;
    }

    private void TransitionToCollectingPlayerSelections(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.CollectingPlayerSelections;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(CollectingPlayerSelectionsDuration);

        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.Stage = GameStage.CollectingPlayerSelections;
        currentInstance.AnsweringStartedAtUtc = now;

        // Establish eligible players
        foreach (var player in session.Room.Players.Where(p => p.IsConnected))
        {
            dbContext.GameQuestionEligiblePlayers.Add(new GameQuestionEligiblePlayer
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = currentInstance.Id,
                PlayerId = player.Id
            });
        }
    }

    private void TransitionToShowingQuestionResults(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.ShowingQuestionResults;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(ShowingQuestionResultsDuration);

        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.Stage = GameStage.ShowingQuestionResults;
        currentInstance.AnsweringEndsAtUtc = now;
        currentInstance.ResultsStartedAtUtc = now;
    }

    private void TransitionToCollectingTextAnswers(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.CollectingTextAnswers;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(CollectingTextAnswersDuration);

        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.Stage = GameStage.CollectingTextAnswers;
        currentInstance.AnsweringStartedAtUtc = now;

        foreach (var player in session.Room.Players.Where(p => p.IsConnected && currentInstance.SubjectPlayerId != p.Id))
        {
            dbContext.Database.ExecuteSqlInterpolated($"""
                INSERT OR IGNORE INTO TextAnswerEligiblePlayers (Id, QuestionInstanceId, PlayerId)
                VALUES ({Guid.NewGuid()}, {currentInstance.Id}, {player.Id})
                """);
        }
    }

    private void TransitionToRevealingTextAnswers(GameSession session, DateTimeOffset now)
    {
        var currentInstance = GetCurrentQuestionInstance(session);
        var answersCount = dbContext.TextAnswerSubmissions.Count(s => s.QuestionInstanceId == currentInstance.Id);

        session.Stage = GameStage.RevealingTextAnswers;
        session.StageStartedAtUtc = now;
        currentInstance.Stage = GameStage.RevealingTextAnswers;
        currentInstance.AnsweringEndsAtUtc = now;

        var submissions = dbContext.TextAnswerSubmissions.Where(s => s.QuestionInstanceId == currentInstance.Id && s.RevealOrder == null).ToList();
        if (submissions.Count > 0)
        {
            randomProvider.Shuffle(submissions);
            var maxExisting = dbContext.TextAnswerSubmissions.Where(s => s.QuestionInstanceId == currentInstance.Id && s.RevealOrder != null).Max(s => (int?)s.RevealOrder) ?? -1;
            for (int i = 0; i < submissions.Count; i++)
            {
                submissions[i].RevealOrder = maxExisting + 1 + i;
            }
        }

        var baseSeconds = options.Value.TextAnswerRevealBaseSeconds;
        var perAnswerSeconds = options.Value.TextAnswerRevealPerAnswerSeconds;
        var maxSeconds = options.Value.TextAnswerRevealMaximumSeconds;
        var duration = Math.Min(baseSeconds + answersCount * perAnswerSeconds, maxSeconds);
        session.StageEndsAtUtc = now.Add(TimeSpan.FromSeconds(duration));
    }

    private void TransitionToCollectingTextAnswerVotes(GameSession session, DateTimeOffset now)
    {
        var currentInstance = GetCurrentQuestionInstance(session);

        var answersCount = dbContext.TextAnswerSubmissions.Count(s => s.QuestionInstanceId == currentInstance.Id);
        if (answersCount <= 1)
        {
            // Skip Voting
            TransitionToShowingTextAnswerResults(session, now);
            return;
        }

        session.Stage = GameStage.CollectingTextAnswerVotes;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(CollectingTextAnswerVotesDuration);

        currentInstance.Stage = GameStage.CollectingTextAnswerVotes;

        foreach (var player in session.Room.Players.Where(p => p.IsConnected))
        {
            dbContext.TextAnswerVoteEligiblePlayers.Add(new TextAnswerVoteEligiblePlayer
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = currentInstance.Id,
                PlayerId = player.Id
            });
        }
    }

    private void TransitionToShowingTextAnswerResults(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.ShowingTextAnswerResults;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.Add(ShowingTextAnswerResultsDuration);

        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.Stage = GameStage.ShowingTextAnswerResults;
        currentInstance.ResultsStartedAtUtc = now;
    }

    private void TransitionToCollectingPhotoAnswers(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.CollectingPhotoAnswers;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.AddSeconds(options.Value.PhotoAnswerSubmissionSeconds);
        var instance = GetCurrentQuestionInstance(session);
        instance.Stage = GameStage.CollectingPhotoAnswers;
        instance.AnsweringStartedAtUtc = now;
        foreach (var player in session.Room.Players.Where(p => p.IsConnected))
        {
            dbContext.PhotoAnswerEligiblePlayers.Add(new PhotoAnswerEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, PlayerId = player.Id });
        }
    }

    private void TransitionFromCollectingPhotoAnswers(GameSession session, DateTimeOffset now)
    {
        var instance = GetCurrentQuestionInstance(session);
        var submissions = dbContext.PhotoAnswerSubmissions.Where(s => s.QuestionInstanceId == instance.Id).ToList();
        instance.AnsweringEndsAtUtc = now;
        if (submissions.Count == 0)
        {
            TransitionToShowingPhotoAnswerResults(session, now);
            return;
        }
        var unordered = submissions.Where(s => s.RevealOrder == null).ToList();
        if (unordered.Count > 0)
        {
            randomProvider.Shuffle(unordered);
            var next = submissions.Where(s => s.RevealOrder != null).Max(s => (int?)s.RevealOrder) ?? -1;
            foreach (var submission in unordered) submission.RevealOrder = ++next;
        }
        session.Stage = GameStage.RevealingPhotoAnswers;
        session.StageStartedAtUtc = now;
        instance.Stage = GameStage.RevealingPhotoAnswers;
        var seconds = Math.Min(options.Value.PhotoAnswerRevealBaseSeconds + submissions.Count * options.Value.PhotoAnswerRevealPerPhotoSeconds, options.Value.PhotoAnswerRevealMaximumSeconds);
        session.StageEndsAtUtc = now.AddSeconds(seconds);
    }

    private void TransitionToCollectingPhotoAnswerVotes(GameSession session, DateTimeOffset now)
    {
        var instance = GetCurrentQuestionInstance(session);
        var count = dbContext.PhotoAnswerSubmissions.Count(s => s.QuestionInstanceId == instance.Id);
        if (count <= 1)
        {
            TransitionToShowingPhotoAnswerResults(session, now);
            return;
        }
        session.Stage = GameStage.CollectingPhotoAnswerVotes;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.AddSeconds(session.Room.Settings.VotingSeconds);
        instance.Stage = GameStage.CollectingPhotoAnswerVotes;
        foreach (var player in session.Room.Players.Where(p => p.IsConnected))
        {
            dbContext.PhotoAnswerVoteEligiblePlayers.Add(new PhotoAnswerVoteEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, PlayerId = player.Id });
        }
    }

    private void TransitionToShowingPhotoAnswerResults(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.ShowingPhotoAnswerResults;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = now.AddSeconds(options.Value.PhotoAnswerResultsSeconds);
        var instance = GetCurrentQuestionInstance(session);
        instance.Stage = GameStage.ShowingPhotoAnswerResults;
        instance.ResultsStartedAtUtc = now;
    }

    private void TransitionToCollectingDrawingAnswers(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.CollectingDrawingAnswers; session.StageStartedAtUtc = now; session.StageEndsAtUtc = now.AddSeconds(options.Value.DrawingAnswerSubmissionSeconds);
        var instance = GetCurrentQuestionInstance(session); instance.Stage = session.Stage; instance.AnsweringStartedAtUtc = now;
        foreach (var player in session.Room.Players.Where(p => p.IsConnected))
        {
            // The background worker and an immediate transition can reach this
            // point concurrently. SQLite's INSERT OR IGNORE makes eligibility
            // creation atomic against the unique (question, player) index.
            dbContext.Database.ExecuteSqlInterpolated($"""
                INSERT OR IGNORE INTO DrawingAnswerEligiblePlayers (Id, QuestionInstanceId, PlayerId)
                VALUES ({Guid.NewGuid()}, {instance.Id}, {player.Id})
                """);
        }
    }
    private void TransitionFromCollectingDrawingAnswers(GameSession session, DateTimeOffset now)
    {
        var instance = GetCurrentQuestionInstance(session);
        var submissions = dbContext.DrawingAnswerSubmissions.Where(s => s.QuestionInstanceId == instance.Id).ToList()
            .Concat(dbContext.DrawingAnswerSubmissions.Local.Where(s => s.QuestionInstanceId == instance.Id))
            .DistinctBy(s => s.Id)
            .ToList();
        instance.AnsweringEndsAtUtc = now;
        if (submissions.Count == 0) { TransitionToShowingDrawingAnswerResults(session, now); return; }
        var unordered = submissions.Where(s => s.RevealOrder == null).ToList(); randomProvider.Shuffle(unordered); var next = submissions.Where(s => s.RevealOrder != null).Max(s => (int?)s.RevealOrder) ?? -1; foreach (var s in unordered) { if (s.RevealOrder == null) s.RevealOrder = ++next; }
        session.Stage = GameStage.RevealingDrawingAnswers; session.StageStartedAtUtc = now; instance.Stage = session.Stage;
        session.StageEndsAtUtc = now.AddSeconds(Math.Min(options.Value.DrawingAnswerRevealBaseSeconds + submissions.Count * options.Value.DrawingAnswerRevealPerDrawingSeconds, options.Value.DrawingAnswerRevealMaximumSeconds));
    }
    private void TransitionToCollectingDrawingAnswerVotes(GameSession session, DateTimeOffset now)
    {
        var instance = GetCurrentQuestionInstance(session);
        var submissionIds = dbContext.DrawingAnswerSubmissions.Where(s => s.QuestionInstanceId == instance.Id).Select(s => s.Id).ToList();
        submissionIds.AddRange(dbContext.DrawingAnswerSubmissions.Local.Where(s => s.QuestionInstanceId == instance.Id).Select(s => s.Id));
        var count = submissionIds.Distinct().Count();
        if (count <= 1) { TransitionToShowingDrawingAnswerResults(session, now); return; }
        session.Stage = GameStage.CollectingDrawingAnswerVotes; session.StageStartedAtUtc = now; session.StageEndsAtUtc = now.AddSeconds(session.Room.Settings.VotingSeconds); instance.Stage = session.Stage;
        foreach (var player in session.Room.Players.Where(p => p.IsConnected)) if (!dbContext.DrawingAnswerVoteEligiblePlayers.Local.Any(e => e.QuestionInstanceId == instance.Id && e.PlayerId == player.Id) && !dbContext.DrawingAnswerVoteEligiblePlayers.Any(e => e.QuestionInstanceId == instance.Id && e.PlayerId == player.Id)) dbContext.DrawingAnswerVoteEligiblePlayers.Add(new DrawingAnswerVoteEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, PlayerId = player.Id });
    }
    private void TransitionToShowingDrawingAnswerResults(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.ShowingDrawingAnswerResults; session.StageStartedAtUtc = now; session.StageEndsAtUtc = now.AddSeconds(options.Value.DrawingAnswerResultsSeconds); var instance = GetCurrentQuestionInstance(session); instance.Stage = session.Stage; instance.ResultsStartedAtUtc = now;
    }

    private bool TransitionFromResults(GameSession session, DateTimeOffset now)
    {
        var currentInstance = GetCurrentQuestionInstance(session);
        currentInstance.CompletedAtUtc = now;

        if (session.CurrentQuestionNumber < session.QuestionsInCurrentRound)
        {
            // Next Question
            session.CurrentQuestionNumber++;
            var currentRound = session.Rounds.First(r => r.RoundNumber == session.CurrentRoundNumber);
            var nextQuestion = currentRound.Questions.First(q => q.QuestionNumber == session.CurrentQuestionNumber);
            session.CurrentQuestionInstanceId = nextQuestion.Id;

            TransitionToQuestionIntro(session, now);
        }
        else
        {
            // Round Summary
            session.Stage = GameStage.RoundSummary;
            session.StageStartedAtUtc = now;
            session.StageEndsAtUtc = now.Add(RoundSummaryDuration);

            var currentRound = session.Rounds.First(r => r.RoundNumber == session.CurrentRoundNumber);
            currentRound.CompletedAtUtc = now;
        }

        return true;
    }

    private bool TransitionFromRoundSummary(GameSession session, DateTimeOffset now)
    {
        if (session.CurrentRoundNumber < session.TotalRounds)
        {
            // Next Round
            session.CurrentRoundNumber++;
            session.CurrentQuestionNumber = 1;

            var nextRound = session.Rounds.First(r => r.RoundNumber == session.CurrentRoundNumber);
            var nextQuestion = nextRound.Questions.First(q => q.QuestionNumber == session.CurrentQuestionNumber);
            session.CurrentCategoryId = nextRound.CategoryId;
            session.CurrentQuestionInstanceId = nextQuestion.Id;

            session.Stage = GameStage.CategoryIntro;
            session.StageStartedAtUtc = now;
            session.StageEndsAtUtc = now.Add(CategoryIntroDuration);
        }
        else
        {
            // Game Summary
            session.Stage = GameStage.GameSummary;
            session.StageStartedAtUtc = now;
            session.StageEndsAtUtc = now.Add(GameSummaryDuration);
        }

        return true;
    }

    private void TransitionToCompleted(GameSession session, DateTimeOffset now)
    {
        session.Stage = GameStage.Completed;
        session.StageStartedAtUtc = now;
        session.StageEndsAtUtc = null;
        session.CompletedAtUtc = now;
    }

    private GameQuestionInstance GetCurrentQuestionInstance(GameSession session)
    {
        return session.Rounds
            .SelectMany(r => r.Questions)
            .First(q => q.Id == session.CurrentQuestionInstanceId);
    }
}
