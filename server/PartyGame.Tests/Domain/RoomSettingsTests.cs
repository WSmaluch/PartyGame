using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Domain;

public sealed class RoomSettingsTests
{
    [Fact]
    public void Defaults_MatchLobbyContract()
    {
        var settings = new RoomSettings();
        Assert.Equal(4, settings.RoundCount);
        Assert.Equal(5, settings.QuestionsPerRound);
        Assert.Equal(20, settings.PlayerSelectionSeconds);
        Assert.Equal(40, settings.TextAnswerSeconds);
        Assert.Equal(20, settings.VotingSeconds);
        Assert.Equal(45, settings.PhotoSeconds);
        Assert.Equal(90, settings.DrawingSeconds);
        Assert.Equal(8, settings.ResultPresentationSeconds);
        Assert.True(settings.FinalRoundEnabled);
        Assert.Equal(3, settings.FinalDrawingPasses);
        settings.Validate();
    }

    [Fact]
    public void Validate_AcceptsEveryBoundary()
    {
        new RoomSettings { RoundCount = 1, QuestionsPerRound = 4, PlayerSelectionSeconds = 5, TextAnswerSeconds = 5, VotingSeconds = 5, PhotoSeconds = 10, DrawingSeconds = 30, ResultPresentationSeconds = 3, FinalDrawingPasses = 1 }.Validate();
        new RoomSettings { RoundCount = 10, QuestionsPerRound = 6, PlayerSelectionSeconds = 120, TextAnswerSeconds = 180, VotingSeconds = 120, PhotoSeconds = 180, DrawingSeconds = 300, ResultPresentationSeconds = 30, FinalDrawingPasses = 9 }.Validate();
    }

    [Fact]
    public void Validate_RejectsValuesOutsideEveryBoundary()
    {
        var invalidSettings = new RoomSettings[]
        {
            new() { RoundCount = 0 }, new() { RoundCount = 11 },
            new() { QuestionsPerRound = 3 }, new() { QuestionsPerRound = 7 },
            new() { PlayerSelectionSeconds = 4 }, new() { PlayerSelectionSeconds = 121 },
            new() { TextAnswerSeconds = 4 }, new() { TextAnswerSeconds = 181 },
            new() { VotingSeconds = 4 }, new() { VotingSeconds = 121 },
            new() { PhotoSeconds = 9 }, new() { PhotoSeconds = 181 },
            new() { DrawingSeconds = 29 }, new() { DrawingSeconds = 301 },
            new() { ResultPresentationSeconds = 2 }, new() { ResultPresentationSeconds = 31 },
            new() { FinalDrawingPasses = 0 }, new() { FinalDrawingPasses = 10 }
        };
        Assert.All(invalidSettings, settings => Assert.Throws<DomainValidationException>(settings.Validate));
    }
}
