using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Rooms;

public sealed class GamePlanner(PartyGameDbContext dbContext, IRandomProvider randomProvider)
{
    public async Task<bool> TryCreatePlanAsync(GameRoom room, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var enabledTypes = room.EnabledQuestionTypes;
        if (enabledTypes == null || enabledTypes.Count == 0)
        {
            enabledTypes = new List<QuestionType> { QuestionType.PlayerSelection };
        }

        var packages = await dbContext.GamePackages
            .Include(p => p.Categories.Where(c => c.IsActive))
                .ThenInclude(c => c.Questions.Where(q => q.IsActive && enabledTypes.Contains(q.Type) && q.MinimumPlayers <= room.Players.Count))
            .Where(p => room.ContentPackageVersionId.HasValue
                ? p.Id == room.ContentPackageVersionId.Value
                : room.SelectedPackageKeys.Contains(p.Key) && p.IsActive)
            .ToListAsync(cancellationToken);

        var validCategories = new List<GameCategory>();
        int perTypeBase = room.Settings.QuestionsPerRound / enabledTypes.Count;
        int remainder = room.Settings.QuestionsPerRound % enabledTypes.Count;

        foreach (var package in packages)
        {
            foreach (var category in package.Categories)
            {
                var questionsByType = category.Questions.GroupBy(q => q.Type).ToDictionary(g => g.Key, g => g.ToList());
                bool isValid = true;
                var availableCounts = enabledTypes.Select(type => questionsByType.GetValueOrDefault(type)?.Count ?? 0).OrderBy(count => count).ToArray();
                foreach (var type in enabledTypes)
                {
                    int count = questionsByType.ContainsKey(type) ? questionsByType[type].Count : 0;
                    if (count < perTypeBase)
                    {
                        isValid = false;
                        break;
                    }
                }
                if (isValid && availableCounts.Sum(count => Math.Max(0, count - perTypeBase)) < remainder) isValid = false;

                if (isValid && category.Questions.Count >= room.Settings.QuestionsPerRound)
                {
                    validCategories.Add(category);
                }
            }
        }

        if (validCategories.Count < room.Settings.RoundCount)
        {
            return false;
        }

        randomProvider.Shuffle(validCategories);
        var selectedCategories = validCategories.Take(room.Settings.RoundCount).ToList();

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            RoomId = room.Id,
            Stage = GameStage.CategoryIntro,
            CurrentRoundNumber = 1,
            // The Final Round is exposed as the extra N+1 round, but no normal
            // GameRound row is created for it. TransitionFromRoundSummary uses
            // the persisted normal-round collection to choose the next branch.
            TotalRounds = room.Settings.RoundCount + (room.Settings.FinalRoundEnabled ? 1 : 0),
            CurrentQuestionNumber = 1,
            QuestionsInCurrentRound = room.Settings.QuestionsPerRound,
            StartedAtUtc = now,
            StageStartedAtUtc = now,
            StageEndsAtUtc = now.AddSeconds(5), // Intro time
            Room = room
        };

        var subjectCounts = room.Players.ToDictionary(p => p.Id, p => 0);
        Guid? lastSubjectId = null;

        for (int i = 0; i < selectedCategories.Count; i++)
        {
            var category = selectedCategories[i];
            var round = new GameRound
            {
                Id = Guid.NewGuid(),
                GameSessionId = session.Id,
                RoundNumber = i + 1,
                CategoryId = category.Id,
                StartedAtUtc = now,
                Session = session,
                Category = category
            };
            session.Rounds.Add(round);

            var categoryQuestionsByType = category.Questions.GroupBy(q => q.Type).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var kvp in categoryQuestionsByType) randomProvider.Shuffle(kvp.Value);

            var extraOrder = enabledTypes.ToList();
            randomProvider.Shuffle(extraOrder);
            var roundPattern = new List<QuestionType>();
            foreach (var type in enabledTypes)
                roundPattern.AddRange(Enumerable.Repeat(type, perTypeBase));
            foreach (var type in extraOrder.Where(type => categoryQuestionsByType.GetValueOrDefault(type)?.Count > perTypeBase).Take(remainder))
                roundPattern.Add(type);
            randomProvider.Shuffle(roundPattern);

            var selectedQuestions = new List<GameQuestion>();
            var catQuestionsRemaining = category.Questions.GroupBy(q => q.Type).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var kvp in catQuestionsRemaining) randomProvider.Shuffle(kvp.Value);

            foreach (var type in roundPattern)
            {
                var q = catQuestionsRemaining[type].First();
                catQuestionsRemaining[type].Remove(q);
                selectedQuestions.Add(q);
            }

            for (int j = 0; j < selectedQuestions.Count; j++)
            {
                var question = selectedQuestions[j];
                var instance = new GameQuestionInstance
                {
                    Id = Guid.NewGuid(),
                    RoundId = round.Id,
                    QuestionId = question.Id,
                    QuestionNumber = j + 1,
                    Stage = GameStage.CategoryIntro,
                    StartedAtUtc = now,
                    Round = round,
                    Question = question
                };

                if (question.Type == QuestionType.TextAnswer)
                {
                    var minCount = subjectCounts.Values.Min();
                    var candidates = subjectCounts.Where(kvp => kvp.Value == minCount).Select(kvp => kvp.Key).ToList();
                    if (candidates.Count > 1 && lastSubjectId.HasValue && candidates.Contains(lastSubjectId.Value))
                    {
                        candidates.Remove(lastSubjectId.Value);
                    }
                    randomProvider.Shuffle(candidates);
                    var subjectId = candidates.First();
                    subjectCounts[subjectId]++;
                    lastSubjectId = subjectId;
                    instance.SubjectPlayerId = subjectId;
                }

                round.Questions.Add(instance);
            }
        }

        var firstRound = session.Rounds.First();
        var firstQuestion = firstRound.Questions.First();

        session.CurrentCategoryId = firstRound.CategoryId;
        session.CurrentQuestionInstanceId = firstQuestion.Id;

        dbContext.GameSessions.Add(session);
        room.Session = session;

        return true;
    }
}
