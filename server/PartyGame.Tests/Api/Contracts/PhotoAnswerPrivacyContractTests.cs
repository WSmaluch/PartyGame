using System.Text.Json;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Api.Contracts;

public sealed class PhotoAnswerPrivacyContractTests
{
    [Fact]
    public void CollectingSnapshot_DoesNotExposePhotoIdentifiersOrUrls()
    {
        var room = CreateRoom(GameStage.CollectingPhotoAnswers);
        var json = JsonSerializer.Serialize(room.ToSnapshot(), JsonSerializerOptions.Web);
        Assert.DoesNotContain("photoAnswerId", json);
        Assert.DoesNotContain("displayPhotoUrl", json);
        Assert.DoesNotContain("mediaAssetId", json);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorPlayerId", json);
    }

    [Fact]
    public void RevealSnapshot_ContainsOnlyAnonymousPhotoData()
    {
        var room = CreateRoom(GameStage.RevealingPhotoAnswers);
        var json = JsonSerializer.Serialize(room.ToSnapshot(), JsonSerializerOptions.Web);
        Assert.Contains("photoAnswerId", json);
        Assert.Contains("displayPhotoUrl", json);
        Assert.Contains("displayOrder", json);
        Assert.DoesNotContain("authorPlayerId", json);
        Assert.DoesNotContain("voteCount", json);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultsSnapshot_RevealsAuthorsVotersAndLedgerPoints()
    {
        var room = CreateRoom(GameStage.ShowingPhotoAnswerResults);
        var instance = room.Session!.Rounds[0].Questions[0];
        var voter = room.Players[1];
        instance.PhotoAnswerVoteEligiblePlayers.Add(new() { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, PlayerId = voter.Id });
        instance.PhotoAnswerVotes.Add(new() { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, VoterPlayerId = voter.Id, SelectedPhotoAnswerId = instance.PhotoAnswerSubmissions[0].Id });
        room.Session.ScoreTransactions.Add(new() { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, GameSessionId = room.Session.Id, PlayerId = voter.Id, Points = 100, Reason = "PhotoAnswerConformity" });
        var json = JsonSerializer.Serialize(room.ToSnapshot(), JsonSerializerOptions.Web);
        Assert.Contains("authorPlayerId", json);
        Assert.Contains("pointsAwarded", json);
        Assert.Contains("100", json);
        Assert.DoesNotContain("mediaAssetId", json);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
    }

    private static GameRoom CreateRoom(GameStage stage)
    {
        var author = new Player { Id = Guid.NewGuid(), Nickname = "Author", NormalizedNickname = "AUTHOR", HasProfilePhoto = true };
        var voter = new Player { Id = Guid.NewGuid(), Nickname = "Voter", NormalizedNickname = "VOTER" };
        var room = new GameRoom { Id = Guid.NewGuid(), Code = "ABCD", Players = [author, voter] };
        var category = new GameCategory { Id = Guid.NewGuid(), NamePl = "Test", NameEn = "Test" };
        var definition = new GameQuestion { Id = Guid.NewGuid(), Type = QuestionType.PhotoAnswer, TextPl = "Zdjęcie", TextEn = "Photo" };
        var instance = new GameQuestionInstance { Id = Guid.NewGuid(), Question = definition, QuestionId = definition.Id, Stage = stage };
        instance.PhotoAnswerEligiblePlayers.Add(new() { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, PlayerId = author.Id });
        var asset = new MediaAsset { Id = Guid.NewGuid(), Width = 800, Height = 600, DisplayStorageKey = "private/display.jpg", ThumbnailStorageKey = "private/thumb.jpg" };
        instance.PhotoAnswerSubmissions.Add(new() { Id = Guid.NewGuid(), QuestionInstanceId = instance.Id, AuthorPlayerId = author.Id, MediaAssetId = asset.Id, MediaAsset = asset, RevealOrder = 0 });
        var round = new GameRound { Id = Guid.NewGuid(), RoundNumber = 1, Category = category, CategoryId = category.Id, Questions = [instance] };
        var session = new GameSession { Id = Guid.NewGuid(), Room = room, RoomId = room.Id, Stage = stage, CurrentRoundNumber = 1, TotalRounds = 1, CurrentQuestionNumber = 1, QuestionsInCurrentRound = 1, CurrentQuestionInstanceId = instance.Id, Rounds = [round] };
        room.Session = session;
        return room;
    }
}
