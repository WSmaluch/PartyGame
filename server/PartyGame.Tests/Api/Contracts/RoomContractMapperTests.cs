using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Api.Contracts;

public class RoomContractMapperTests
{
    private (GameSession session, GameRoom room, Guid ania, Guid wojtek, Guid kasia) SetupTestEnvironment()
    {
        var aniaId = Guid.NewGuid();
        var wojtekId = Guid.NewGuid();
        var kasiaId = Guid.NewGuid();

        var room = new GameRoom
        {
            Id = Guid.NewGuid(),
            Code = "TEST",
            Players = new List<Player>
            {
                new Player { Id = aniaId, Nickname = "Ania", Score = 100 },
                new Player { Id = wojtekId, Nickname = "Wojtek", Score = 50 },
                new Player { Id = kasiaId, Nickname = "Kasia", Score = 0 }
            }
        };

        var questionId = Guid.NewGuid();
        var category = new GameCategory { Id = Guid.NewGuid(), NamePl = "Kat", NameEn = "Cat", DescriptionPl = "Opis", DescriptionEn = "Desc" };
        var questionDef = new GameQuestion { Id = Guid.NewGuid(), TextPl = "Q", TextEn = "Q" };

        var instance = new GameQuestionInstance
        {
            Id = questionId,
            Question = questionDef,
            EligiblePlayers = new List<GameQuestionEligiblePlayer>
            {
                new GameQuestionEligiblePlayer { PlayerId = aniaId },
                new GameQuestionEligiblePlayer { PlayerId = wojtekId },
                new GameQuestionEligiblePlayer { PlayerId = kasiaId }
            },
            Answers = new List<PlayerSelectionAnswer>
            {
                new PlayerSelectionAnswer { VoterPlayerId = aniaId, SelectedPlayerId = wojtekId, PointsAwarded = 100 },
                new PlayerSelectionAnswer { VoterPlayerId = wojtekId, SelectedPlayerId = wojtekId, PointsAwarded = 200 } // Kasia has not voted
            }
        };

        var round = new GameRound
        {
            Id = Guid.NewGuid(),
            RoundNumber = 1,
            Category = category,
            Questions = new List<GameQuestionInstance> { instance }
        };

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            Room = room,
            CurrentRoundNumber = 1,
            CurrentQuestionInstanceId = questionId,
            Rounds = new List<GameRound> { round }
        };

        room.Session = session;
        return (session, room, aniaId, wojtekId, kasiaId);
    }

    [Fact]
    public void ToSnapshot_CollectingPlayerSelections_DoesNotLeakVotes()
    {
        // Arrange
        var env = SetupTestEnvironment();
        env.session.Stage = GameStage.CollectingPlayerSelections;

        // Act
        var snapshot = env.session.ToSnapshot();

        // Assert
        Assert.Equal("CollectingPlayerSelections", snapshot.Stage);

        Assert.NotNull(snapshot.AnsweredPlayerIds);
        Assert.Contains(env.ania, snapshot.AnsweredPlayerIds);
        Assert.Contains(env.wojtek, snapshot.AnsweredPlayerIds);
        Assert.DoesNotContain(env.kasia, snapshot.AnsweredPlayerIds);

        Assert.Equal(2, snapshot.AnsweredPlayers);
        Assert.Equal(3, snapshot.RequiredPlayers);

        // This is the data privacy test!
        Assert.Null(snapshot.Results);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        Assert.DoesNotContain("Results", json);
        Assert.DoesNotContain("SelectedPlayerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VoteCount", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AnsweredPlayerIds", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSnapshot_ShowingQuestionResults_RevealsVotesAndPoints()
    {
        // Arrange
        var env = SetupTestEnvironment();
        env.session.Stage = GameStage.ShowingQuestionResults;

        // Act
        var snapshot = env.session.ToSnapshot();

        // Assert
        Assert.Equal("ShowingQuestionResults", snapshot.Stage);

        Assert.NotNull(snapshot.Results);
        Assert.Equal(env.session.CurrentQuestionInstanceId, snapshot.Results.QuestionInstanceId);

        Assert.Equal(2, snapshot.Results.AnsweredPlayers);
        Assert.Equal(3, snapshot.Results.RequiredPlayers);
        Assert.Equal(1, snapshot.Results.MissingPlayers);

        // Wojtek got 2 votes
        var optionWojtek = snapshot.Results.Options.FirstOrDefault(o => o.SelectedPlayerId == env.wojtek);
        Assert.NotNull(optionWojtek);
        Assert.Equal(2, optionWojtek.VoteCount);
        Assert.True(optionWojtek.IsTopResult);

        // Check PointsAwarded is serialized
        var voterAnia = optionWojtek.Voters.FirstOrDefault(v => v.PlayerId == env.ania);
        Assert.NotNull(voterAnia);
        Assert.Equal(100, voterAnia.PointsAwarded);

        var voterWojtek = optionWojtek.Voters.FirstOrDefault(v => v.PlayerId == env.wojtek);
        Assert.NotNull(voterWojtek);
        Assert.Equal(200, voterWojtek.PointsAwarded);

        // Ranking should be populated
        Assert.NotNull(snapshot.Ranking);
        Assert.Equal(3, snapshot.Ranking.Count);
        Assert.Equal(env.ania, snapshot.Ranking[0].PlayerId); // 100
        Assert.Equal(env.wojtek, snapshot.Ranking[1].PlayerId); // 50
        Assert.Equal(env.kasia, snapshot.Ranking[2].PlayerId); // 0
    }
}
