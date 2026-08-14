using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Api.Contracts;

public class RoomContractMapperTests
{
    public static IEnumerable<object[]> RoundSummaryRankCases()
    {
        yield return [0, 500, 0, 2, 1, 2];
        yield return [100, 100, 50, 1, 1, 3];
        yield return [100, 100, 100, 1, 1, 1];
        yield return [300, 200, 100, 1, 2, 3];
    }

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
    public void ToSnapshot_ExposesPersistedQuestionInstanceIdAlongsideDefinitionId()
    {
        var env = SetupTestEnvironment();
        env.session.Stage = GameStage.QuestionIntro;

        var snapshot = env.session.ToSnapshot();

        Assert.NotNull(snapshot.Question);
        Assert.Equal(env.session.CurrentQuestionInstanceId, snapshot.Question.InstanceId);
        Assert.NotEqual(snapshot.Question.Id, snapshot.Question.InstanceId);
    }

    [Fact]
    public void ToSnapshot_RendersPersistedTextQuestionSubjectWithoutTemplateTokens()
    {
        var env = SetupTestEnvironment();
        var instance = env.session.Rounds.Single().Questions.Single();
        instance.Question.Type = QuestionType.TextAnswer;
        instance.Question.TextPl = "Co {player} na pewno zapomni spakować?";
        instance.Question.TextEn = "What will {player} definitely forget to pack?";
        instance.SubjectPlayerId = env.wojtek;
        env.session.Stage = GameStage.CollectingTextAnswers;

        var snapshot = env.session.ToSnapshot();

        Assert.Equal("Co Wojtek na pewno zapomni spakować?", snapshot.Question!.Text.Pl);
        Assert.Equal("What will Wojtek definitely forget to pack?", snapshot.Question.Text.En);
        Assert.DoesNotContain("{player}", snapshot.Question.Text.Pl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{target}", snapshot.Question.Text.Pl, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [MemberData(nameof(RoundSummaryRankCases))]
    public void ToSnapshot_RoundSummary_UsesCompetitionRanks(
        int aniaScore, int wojtekScore, int kasiaScore,
        int expectedAniaRank, int expectedWojtekRank, int expectedKasiaRank)
    {
        var env = SetupTestEnvironment();
        env.session.Stage = GameStage.RoundSummary;
        env.room.Players.Single(player => player.Id == env.ania).Score = aniaScore;
        env.room.Players.Single(player => player.Id == env.wojtek).Score = wojtekScore;
        env.room.Players.Single(player => player.Id == env.kasia).Score = kasiaScore;

        var snapshot = env.session.ToSnapshot();

        var ranks = snapshot.RoundSummary!.Ranking.ToDictionary(entry => entry.PlayerId, entry => entry.Rank);
        Assert.Equal(expectedAniaRank, ranks[env.ania]);
        Assert.Equal(expectedWojtekRank, ranks[env.wojtek]);
        Assert.Equal(expectedKasiaRank, ranks[env.kasia]);
    }

    [Fact]
    public void ToSnapshot_Completed_PreservesCompetitionRanks()
    {
        var env = SetupTestEnvironment();
        env.session.Stage = GameStage.Completed;
        env.room.Players.Single(player => player.Id == env.ania).Score = 0;
        env.room.Players.Single(player => player.Id == env.wojtek).Score = 500;
        env.room.Players.Single(player => player.Id == env.kasia).Score = 0;

        var snapshot = env.session.ToSnapshot();

        var ranks = snapshot.Ranking!.ToDictionary(entry => entry.PlayerId, entry => entry.Rank);
        Assert.Equal(2, ranks[env.ania]);
        Assert.Equal(1, ranks[env.wojtek]);
        Assert.Equal(2, ranks[env.kasia]);
    }
}
