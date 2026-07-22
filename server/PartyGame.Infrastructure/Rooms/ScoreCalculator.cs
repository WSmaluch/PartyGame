using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Rooms;

public sealed class ScoreCalculator(PartyGameDbContext dbContext)
{
    public async Task CalculateAndApplyScoresAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var currentInstanceId = session.CurrentQuestionInstanceId;
        if (currentInstanceId == null) return;

        var currentInstance = await dbContext.GameQuestionInstances
            .Include(q => q.Answers)
            .Include(q => q.EligiblePlayers)
            .FirstOrDefaultAsync(q => q.Id == currentInstanceId, cancellationToken);

        if (currentInstance == null) return;

        var existingScores = await dbContext.ScoreTransactions
            .Where(t => t.QuestionInstanceId == currentInstanceId)
            .ToListAsync(cancellationToken);

        if (existingScores.Any()) return; // Idempotent: already calculated

        // Count votes per selected player
        var voteCounts = currentInstance.Answers
            .GroupBy(a => a.SelectedPlayerId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var answer in currentInstance.Answers)
        {
            if (voteCounts.TryGetValue(answer.SelectedPlayerId, out var votesForSelected))
            {
                var points = votesForSelected * 100;

                if (points > 0)
                {
                    dbContext.ScoreTransactions.Add(new ScoreTransaction
                    {
                        Id = Guid.NewGuid(),
                        GameSessionId = session.Id,
                        QuestionInstanceId = currentInstanceId.Value,
                        PlayerId = answer.VoterPlayerId,
                        Points = points,
                        Reason = "Player Selection Score",
                        CreatedAtUtc = now
                    });

                    var player = session.Room.Players.FirstOrDefault(p => p.Id == answer.VoterPlayerId);
                    if (player != null)
                    {
                        player.Score += points;
                    }
                    answer.PointsAwarded = points;
                }
            }
        }
    }

    public async Task CalculateAndApplyTextAnswerScoresAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var currentInstanceId = session.CurrentQuestionInstanceId;
        if (currentInstanceId == null) return;

        var currentInstance = await dbContext.GameQuestionInstances
            .Include(q => q.TextAnswerVotes)
            .Include(q => q.TextAnswerVoteEligiblePlayers)
            .FirstOrDefaultAsync(q => q.Id == currentInstanceId, cancellationToken);

        if (currentInstance == null) return;

        var existingScores = await dbContext.ScoreTransactions
            .Where(t => t.QuestionInstanceId == currentInstanceId)
            .ToListAsync(cancellationToken);

        if (existingScores.Any()) return; // Idempotent: already calculated

        var voteCounts = currentInstance.TextAnswerVotes
            .GroupBy(v => v.SelectedTextAnswerId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var vote in currentInstance.TextAnswerVotes)
        {
            if (voteCounts.TryGetValue(vote.SelectedTextAnswerId, out var votesForSelected))
            {
                var points = votesForSelected * 100;

                if (points > 0)
                {
                    dbContext.ScoreTransactions.Add(new ScoreTransaction
                    {
                        Id = Guid.NewGuid(),
                        GameSessionId = session.Id,
                        QuestionInstanceId = currentInstanceId.Value,
                        PlayerId = vote.VoterPlayerId,
                        Points = points,
                        Reason = "Text Answer Score",
                        CreatedAtUtc = now
                    });

                    var player = session.Room.Players.FirstOrDefault(p => p.Id == vote.VoterPlayerId);
                    if (player != null)
                    {
                        player.Score += points;
                    }
                }
            }
        }
    }

    public async Task CalculateAndApplyPhotoAnswerScoresAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.CurrentQuestionInstanceId is not Guid questionId) return;
        if (await dbContext.ScoreTransactions.AnyAsync(t => t.QuestionInstanceId == questionId, cancellationToken)) return;
        var persistedVotes = await dbContext.PhotoAnswerVotes.Where(v => v.QuestionInstanceId == questionId).ToListAsync(cancellationToken);
        var votes = persistedVotes
            .Concat(dbContext.PhotoAnswerVotes.Local.Where(v => v.QuestionInstanceId == questionId))
            .DistinctBy(v => v.Id)
            .ToList();
        var counts = votes.GroupBy(v => v.SelectedPhotoAnswerId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var vote in votes)
        {
            var points = counts[vote.SelectedPhotoAnswerId] * 100;
            var transaction = new ScoreTransaction
            {
                Id = Guid.NewGuid(),
                GameSessionId = session.Id,
                QuestionInstanceId = questionId,
                PlayerId = vote.VoterPlayerId,
                Points = points,
                Reason = "PhotoAnswerConformity",
                CreatedAtUtc = now
            };
            dbContext.ScoreTransactions.Add(transaction);
            if (!session.ScoreTransactions.Contains(transaction)) session.ScoreTransactions.Add(transaction);
            var player = session.Room.Players.FirstOrDefault(p => p.Id == vote.VoterPlayerId);
            if (player != null) player.Score += points;
        }
    }

    public async Task CalculateAndApplyDrawingAnswerScoresAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.CurrentQuestionInstanceId is not Guid questionId || await dbContext.ScoreTransactions.AnyAsync(t => t.QuestionInstanceId == questionId, cancellationToken)) return;
        var persistedVotes = await dbContext.DrawingAnswerVotes.Where(v => v.QuestionInstanceId == questionId).ToListAsync(cancellationToken);
        var votes = persistedVotes
            .Concat(dbContext.DrawingAnswerVotes.Local.Where(v => v.QuestionInstanceId == questionId))
            .DistinctBy(v => v.Id)
            .ToList();
        var counts = votes.GroupBy(v => v.SelectedDrawingAnswerId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var vote in votes) { var points = counts[vote.SelectedDrawingAnswerId] * 100; var transaction = new ScoreTransaction { Id = Guid.NewGuid(), GameSessionId = session.Id, QuestionInstanceId = questionId, PlayerId = vote.VoterPlayerId, Points = points, Reason = "DrawingAnswerConformity", CreatedAtUtc = now }; dbContext.ScoreTransactions.Add(transaction); if (!session.ScoreTransactions.Contains(transaction)) session.ScoreTransactions.Add(transaction); var player = session.Room.Players.FirstOrDefault(p => p.Id == vote.VoterPlayerId); if (player != null) player.Score += points; }
    }
}
