using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;

namespace PartyGame.Api.Contracts;

public static class RoomContractMapper
{
    public static RoomSnapshot ToSnapshot(this GameRoom room, IPhotoMediaUrlProvider? mediaUrls = null) => new(
        room.Code,
        room.Phase,
        room.StateVersion,
        room.DisplayConnected,
        GameRoom.MinimumPlayers,
        GameRoom.MaximumPlayers,
        RoomStartEvaluator.CanStart(room),
        new PublicRoomSettings(
            room.Settings.RoundCount,
            room.Settings.QuestionsPerRound,
            room.Settings.PlayerSelectionSeconds,
            room.Settings.TextAnswerSeconds,
            room.Settings.VotingSeconds,
            room.Settings.PhotoSeconds,
            room.Settings.DrawingSeconds,
            room.Settings.ResultPresentationSeconds,
            room.Settings.FinalRoundEnabled,
            room.Settings.FinalDrawingPasses),
        room.Players.OrderBy(player => player.JoinedAtUtc).Select(player => player.ToPublic(room.Code)).ToArray(),
        room.CreatedAtUtc,
        room.StartedAtUtc,
        room.Session?.ToSnapshot(mediaUrls),
        room.ContentPackageVersionId);

    public static GameSnapshot ToSnapshot(this GameSession session, IPhotoMediaUrlProvider? mediaUrls = null)
    {
        mediaUrls ??= new PhotoMediaUrlProvider();
        var isCompleted = session.Stage == GameStage.Completed;
        var orderedPlayers = session.Room.Players
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Id)
            .ToList();
        var scores = new List<PlayerScoreSnapshot>(orderedPlayers.Count);
        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            var player = orderedPlayers[i];
            var rank = i + 1;
            if (i > 0 && player.Score == scores[i - 1].Score)
            {
                rank = scores[i - 1].Rank!.Value;
            }
            scores.Add(new PlayerScoreSnapshot(player.Id, player.Score, rank));
        }

        GameCategorySnapshot? category = null;
        GameQuestionSnapshot? question = null;
        PlayerSelectionResults? results = null;
        TextAnswerResults? textResults = null;
        PhotoAnswerResultsSnapshot? photoResults = null;
        DrawingAnswerResultsSnapshot? drawingResults = null;
        RoundSummarySnapshot? roundSummary = null;
        List<RankingEntry>? ranking = null;
        List<Guid>? answeredPlayerIds = null;
        int? answeredPlayers = null;
        int? requiredPlayers = null;
        List<Guid>? submittedDrawingAnswerPlayerIds = null;
        int? submittedDrawingAnswers = null;
        int? requiredDrawingAnswers = null;

        var currentRound = session.Rounds.FirstOrDefault(r => r.RoundNumber == session.CurrentRoundNumber);
        if (currentRound != null && currentRound.Category != null)
        {
            category = new GameCategorySnapshot(currentRound.Category.Id, new LocalizedText(currentRound.Category.NamePl, currentRound.Category.NameEn), new LocalizedText(currentRound.Category.DescriptionPl, currentRound.Category.DescriptionEn));

            var currentInstance = currentRound.Questions.FirstOrDefault(q => q.Id == session.CurrentQuestionInstanceId);
            if (currentInstance != null && currentInstance.Question != null)
            {
                question = new GameQuestionSnapshot(currentInstance.Question.Id, new LocalizedText(currentInstance.Question.TextPl, currentInstance.Question.TextEn), currentInstance.Id);

                if (session.Stage == GameStage.CollectingPlayerSelections)
                {
                    answeredPlayerIds = currentInstance.Answers.Select(a => a.VoterPlayerId).ToList();
                    answeredPlayers = currentInstance.Answers.Count;
                    requiredPlayers = currentInstance.EligiblePlayers.Count;
                }

                if (session.Stage == GameStage.ShowingQuestionResults || session.Stage == GameStage.Completed)
                {
                    var allVotes = currentInstance.Answers.GroupBy(a => a.SelectedPlayerId).ToList();
                    var maxVotes = allVotes.Count > 0 ? allVotes.Max(g => g.Count()) : 0;

                    var options = new List<PlayerSelectionResultOption>();
                    foreach (var voteGroup in allVotes)
                    {
                        var selectedId = voteGroup.Key;
                        var selectedPlayer = session.Room.Players.FirstOrDefault(p => p.Id == selectedId);
                        if (selectedPlayer == null) continue;

                        var votersList = new List<ResultVoter>();
                        foreach (var vote in voteGroup)
                        {
                            var voter = session.Room.Players.FirstOrDefault(p => p.Id == vote.VoterPlayerId);
                            if (voter != null)
                            {
                                votersList.Add(new ResultVoter(voter.Id, voter.Nickname, voter.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{voter.Id}/profile-photo" : null, vote.PointsAwarded ?? 0));
                            }
                        }

                        options.Add(new PlayerSelectionResultOption(selectedPlayer.Id, selectedPlayer.Nickname, selectedPlayer.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{selectedPlayer.Id}/profile-photo" : null, voteGroup.Count(), voteGroup.Count() == maxVotes, votersList));
                    }

                    results = new PlayerSelectionResults(currentInstance.Id, currentInstance.Answers.Count, currentInstance.EligiblePlayers.Count, currentInstance.EligiblePlayers.Count - currentInstance.Answers.Count, maxVotes, options);
                }

                if (session.Stage == GameStage.CollectingTextAnswers)
                {
                    answeredPlayerIds = currentInstance.TextAnswerSubmissions.Select(a => a.AuthorPlayerId).ToList();
                    answeredPlayers = currentInstance.TextAnswerSubmissions.Count;
                    requiredPlayers = currentInstance.TextAnswerEligiblePlayers.Count;
                }

                if (session.Stage == GameStage.RevealingTextAnswers)
                {
                    var options = new List<TextAnswerOptionVoting>();
                    foreach (var sub in currentInstance.TextAnswerSubmissions.OrderBy(s => s.RevealOrder ?? s.SubmittedAtUtc.Ticks))
                    {
                        options.Add(new TextAnswerOptionVoting(sub.Id, sub.Text, sub.RevealOrder));
                    }
                    textResults = new TextAnswerResults(currentInstance.Id, currentInstance.TextAnswerSubmissions.Count, currentInstance.TextAnswerEligiblePlayers.Count, null, null, null, options, null);
                }

                if (session.Stage == GameStage.CollectingTextAnswerVotes)
                {
                    answeredPlayerIds = currentInstance.TextAnswerVotes.Select(v => v.VoterPlayerId).ToList();
                    answeredPlayers = currentInstance.TextAnswerVotes.Count;
                    requiredPlayers = currentInstance.TextAnswerVoteEligiblePlayers.Count;

                    var options = new List<TextAnswerOptionVoting>();
                    foreach (var sub in currentInstance.TextAnswerSubmissions.OrderBy(s => s.RevealOrder ?? s.SubmittedAtUtc.Ticks))
                    {
                        options.Add(new TextAnswerOptionVoting(sub.Id, sub.Text, sub.RevealOrder));
                    }
                    textResults = new TextAnswerResults(currentInstance.Id, currentInstance.TextAnswerSubmissions.Count, currentInstance.TextAnswerEligiblePlayers.Count, null, null, null, options, answeredPlayerIds);
                }

                if (session.Stage == GameStage.ShowingTextAnswerResults || (session.Stage == GameStage.Completed && currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.TextAnswer))
                {
                    var allVotes = currentInstance.TextAnswerVotes.GroupBy(a => a.SelectedTextAnswerId).ToList();
                    var maxVotes = allVotes.Count > 0 ? allVotes.Max(g => g.Count()) : 0;

                    var options = new List<TextAnswerOptionResult>();
                    foreach (var sub in currentInstance.TextAnswerSubmissions.OrderBy(s => s.RevealOrder ?? s.SubmittedAtUtc.Ticks))
                    {
                        var voteGroup = allVotes.FirstOrDefault(g => g.Key == sub.Id);
                        var voteCount = voteGroup?.Count() ?? 0;

                        var author = session.Room.Players.FirstOrDefault(p => p.Id == sub.AuthorPlayerId);

                        var votersList = new List<ResultVoter>();
                        if (voteGroup != null)
                        {
                            foreach (var vote in voteGroup)
                            {
                                var voter = session.Room.Players.FirstOrDefault(p => p.Id == vote.VoterPlayerId);
                                if (voter != null)
                                {
                                    votersList.Add(new ResultVoter(voter.Id, voter.Nickname, voter.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{voter.Id}/profile-photo" : null, voteCount * 100));
                                }
                            }
                        }

                        options.Add(new TextAnswerOptionResult(sub.Id, sub.Text, author?.Id ?? Guid.Empty, author?.Nickname ?? "", author?.HasProfilePhoto == true ? $"/api/rooms/{session.Room.Code}/players/{author.Id}/profile-photo" : null, voteCount, voteCount > 0 && voteCount == maxVotes, votersList));
                    }
                    textResults = new TextAnswerResults(currentInstance.Id, currentInstance.TextAnswerSubmissions.Count, currentInstance.TextAnswerEligiblePlayers.Count, currentInstance.TextAnswerEligiblePlayers.Count - currentInstance.TextAnswerSubmissions.Count, maxVotes, options, null, null);
                }

                if (session.Stage == GameStage.CollectingPhotoAnswers)
                {
                    answeredPlayerIds = currentInstance.PhotoAnswerSubmissions.Select(s => s.AuthorPlayerId).ToList();
                    answeredPlayers = currentInstance.PhotoAnswerSubmissions.Count;
                    requiredPlayers = currentInstance.PhotoAnswerEligiblePlayers.Count;
                    photoResults = new PhotoAnswerResultsSnapshot(currentInstance.Id, answeredPlayers.Value, requiredPlayers.Value, null, null, null, null, null);
                }

                if (session.Stage is GameStage.RevealingPhotoAnswers or GameStage.CollectingPhotoAnswerVotes)
                {
                    var anonymous = currentInstance.PhotoAnswerSubmissions.OrderBy(s => s.RevealOrder).Select(s =>
                        new AnonymousPhotoAnswer(s.Id, mediaUrls.Display(s.MediaAssetId), mediaUrls.Thumbnail(s.MediaAssetId), s.RevealOrder ?? 0, s.MediaAsset.Width, s.MediaAsset.Height)).ToList();
                    int? voted = session.Stage == GameStage.CollectingPhotoAnswerVotes ? currentInstance.PhotoAnswerVotes.Count : null;
                    int? voters = session.Stage == GameStage.CollectingPhotoAnswerVotes ? currentInstance.PhotoAnswerVoteEligiblePlayers.Count : null;
                    if (session.Stage == GameStage.CollectingPhotoAnswerVotes)
                    {
                        answeredPlayerIds = currentInstance.PhotoAnswerVotes.Select(v => v.VoterPlayerId).ToList();
                        answeredPlayers = voted;
                        requiredPlayers = voters;
                    }
                    photoResults = new PhotoAnswerResultsSnapshot(currentInstance.Id, currentInstance.PhotoAnswerSubmissions.Count, currentInstance.PhotoAnswerEligiblePlayers.Count, voted, voters, null, null, null, AnonymousOptions: anonymous);
                }

                if (session.Stage == GameStage.ShowingPhotoAnswerResults || (session.Stage == GameStage.Completed && currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.PhotoAnswer))
                {
                    var voteGroups = currentInstance.PhotoAnswerVotes.GroupBy(v => v.SelectedPhotoAnswerId).ToDictionary(g => g.Key, g => g.ToList());
                    var maxVotes = voteGroups.Count == 0 ? 0 : voteGroups.Max(g => g.Value.Count);
                    var photoOptions = currentInstance.PhotoAnswerSubmissions.OrderBy(s => s.RevealOrder).Select(submission =>
                    {
                        var author = session.Room.Players.First(p => p.Id == submission.AuthorPlayerId);
                        var votes = voteGroups.GetValueOrDefault(submission.Id) ?? [];
                        var voters = votes.Select(vote =>
                        {
                            var voter = session.Room.Players.First(p => p.Id == vote.VoterPlayerId);
                            var points = session.ScoreTransactions.FirstOrDefault(t => t.QuestionInstanceId == currentInstance.Id && t.PlayerId == voter.Id && t.Reason == "PhotoAnswerConformity")?.Points ?? 0;
                            return new PhotoAnswerResultVoter(voter.Id, voter.Nickname, voter.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{voter.Id}/profile-photo" : null, points);
                        }).ToList();
                        return new PhotoAnswerResultOption(submission.Id, mediaUrls.Display(submission.MediaAssetId), mediaUrls.Thumbnail(submission.MediaAssetId), submission.MediaAsset.Width, submission.MediaAsset.Height, author.Id, author.Nickname, author.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{author.Id}/profile-photo" : null, votes.Count, votes.Count > 0 && votes.Count == maxVotes, voters);
                    }).ToList();
                    photoResults = new PhotoAnswerResultsSnapshot(currentInstance.Id, currentInstance.PhotoAnswerSubmissions.Count, currentInstance.PhotoAnswerEligiblePlayers.Count, currentInstance.PhotoAnswerVotes.Count, currentInstance.PhotoAnswerVoteEligiblePlayers.Count, currentInstance.PhotoAnswerEligiblePlayers.Count - currentInstance.PhotoAnswerSubmissions.Count, currentInstance.PhotoAnswerVoteEligiblePlayers.Count - currentInstance.PhotoAnswerVotes.Count, maxVotes, photoOptions);
                }

                if (session.Stage == GameStage.CollectingDrawingAnswers)
                {
                    answeredPlayerIds = currentInstance.DrawingAnswerSubmissions.Select(s => s.AuthorPlayerId).ToList(); answeredPlayers = currentInstance.DrawingAnswerSubmissions.Count; requiredPlayers = currentInstance.DrawingAnswerEligiblePlayers.Count;
                    submittedDrawingAnswerPlayerIds = answeredPlayerIds;
                    submittedDrawingAnswers = answeredPlayers;
                    requiredDrawingAnswers = requiredPlayers;
                    drawingResults = new DrawingAnswerResultsSnapshot(currentInstance.Id, answeredPlayers.Value, requiredPlayers.Value, null, null, null, null, null);
                }
                if (session.Stage is GameStage.RevealingDrawingAnswers or GameStage.CollectingDrawingAnswerVotes)
                {
                    var anonymous = currentInstance.DrawingAnswerSubmissions.OrderBy(s => s.RevealOrder).Select(s => new AnonymousDrawingAnswer(s.Id, mediaUrls.Display(s.MediaAssetId), mediaUrls.Thumbnail(s.MediaAssetId), s.MediaAsset.Width, s.MediaAsset.Height, session.Stage == GameStage.RevealingDrawingAnswers ? s.RevealOrder : null, session.Stage == GameStage.CollectingDrawingAnswerVotes ? s.RevealOrder : null)).ToList();
                    var voted = session.Stage == GameStage.CollectingDrawingAnswerVotes ? currentInstance.DrawingAnswerVotes.Count : (int?)null; var voters = session.Stage == GameStage.CollectingDrawingAnswerVotes ? currentInstance.DrawingAnswerVoteEligiblePlayers.Count : (int?)null;
                    if (session.Stage == GameStage.CollectingDrawingAnswerVotes) { answeredPlayerIds = currentInstance.DrawingAnswerVotes.Select(v => v.VoterPlayerId).ToList(); answeredPlayers = voted; requiredPlayers = voters; }
                    drawingResults = new DrawingAnswerResultsSnapshot(currentInstance.Id, currentInstance.DrawingAnswerSubmissions.Count, currentInstance.DrawingAnswerEligiblePlayers.Count, voted, voters, null, null, null, AnonymousOptions: anonymous);
                }
                if (session.Stage == GameStage.ShowingDrawingAnswerResults || (session.Stage == GameStage.Completed && currentInstance.Question.Type == PartyGame.Domain.Content.QuestionType.DrawingAnswer))
                {
                    var groups = currentInstance.DrawingAnswerVotes.GroupBy(v => v.SelectedDrawingAnswerId).ToDictionary(g => g.Key, g => g.ToList()); var max = groups.Count == 0 ? 0 : groups.Max(g => g.Value.Count);
                    var options = currentInstance.DrawingAnswerSubmissions.OrderBy(s => s.RevealOrder).Select(s => { var author = session.Room.Players.First(p => p.Id == s.AuthorPlayerId); var votes = groups.GetValueOrDefault(s.Id) ?? []; var voters = votes.Select(v => { var p = session.Room.Players.First(x => x.Id == v.VoterPlayerId); var points = session.ScoreTransactions.FirstOrDefault(t => t.QuestionInstanceId == currentInstance.Id && t.PlayerId == p.Id && t.Reason == "DrawingAnswerConformity")?.Points ?? 0; return new DrawingAnswerResultVoter(p.Id, p.Nickname, p.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{p.Id}/profile-photo" : null, points); }).ToList(); return new DrawingAnswerResultOption(s.Id, mediaUrls.Display(s.MediaAssetId), mediaUrls.Thumbnail(s.MediaAssetId), s.MediaAsset.Width, s.MediaAsset.Height, author.Id, author.Nickname, author.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{author.Id}/profile-photo" : null, votes.Count, votes.Count > 0 && votes.Count == max, voters); }).ToList();
                    drawingResults = new DrawingAnswerResultsSnapshot(currentInstance.Id, currentInstance.DrawingAnswerSubmissions.Count, currentInstance.DrawingAnswerEligiblePlayers.Count, currentInstance.DrawingAnswerVotes.Count, currentInstance.DrawingAnswerVoteEligiblePlayers.Count, currentInstance.DrawingAnswerEligiblePlayers.Count - currentInstance.DrawingAnswerSubmissions.Count, currentInstance.DrawingAnswerVoteEligiblePlayers.Count - currentInstance.DrawingAnswerVotes.Count, max, options);
                }
            }

            if (session.Stage == GameStage.RoundSummary)
            {
                var nextRound = session.Rounds.FirstOrDefault(r => r.RoundNumber == session.CurrentRoundNumber + 1);
                var playerRoundScores = session.Room.Players.Select(p => new PlayerScoreSnapshot(p.Id, p.Score)).ToList(); // In real app, calculate round diff.

                roundSummary = new RoundSummarySnapshot(
                    session.CurrentRoundNumber,
                    category,
                    currentRound.Questions.Count,
                    playerRoundScores,
                    scores.Select(s => new RankingEntry(s.PlayerId, session.Room.Players.First(p => p.Id == s.PlayerId).Nickname, session.Room.Players.First(p => p.Id == s.PlayerId).HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{s.PlayerId}/profile-photo" : null, s.Score, s.Rank ?? 1)).ToList(),
                    nextRound != null,
                    nextRound?.RoundNumber
                );
            }
        }

        if (session.Stage == GameStage.ShowingQuestionResults || session.Stage == GameStage.RoundSummary || session.Stage == GameStage.Completed)
        {
            var ordered = session.Room.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Id).ToList();
            ranking = new List<RankingEntry>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                int rank = i + 1;
                if (i > 0 && p.Score == ranking[i - 1].Score) rank = ranking[i - 1].Rank;
                ranking.Add(new RankingEntry(p.Id, p.Nickname, p.HasProfilePhoto ? $"/api/rooms/{session.Room.Code}/players/{p.Id}/profile-photo" : null, p.Score, rank));
            }
        }

        return new GameSnapshot(
            session.Stage.ToString(),
            session.CurrentRoundNumber,
            session.TotalRounds,
            session.CurrentQuestionNumber,
            session.QuestionsInCurrentRound,
            session.StageEndsAtUtc,
            session.PausedAtUtc,
            session.PausedStage?.ToString(),
            session.PausedRemainingMilliseconds,
            scores,
            session.CompletedAtUtc,
            isCompleted ? session.TotalRounds * session.QuestionsInCurrentRound : null,
            category,
            question,
            results,
            textResults,
            photoResults,
            drawingResults,
            roundSummary,
            ranking,
            answeredPlayerIds,
            answeredPlayers,
            requiredPlayers,
            submittedDrawingAnswerPlayerIds,
            submittedDrawingAnswers,
            requiredDrawingAnswers
        );
    }

    public static PublicPlayer ToPublic(this Player player, string roomCode) => new(
        player.Id,
        player.Nickname,
        player.IsHost,
        player.IsReady,
        player.IsConnected,
        player.HasProfilePhoto,
        player.HasProfilePhoto ? $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo" : null,
        player.Score);
}
