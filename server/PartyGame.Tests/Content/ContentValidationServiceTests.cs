using PartyGame.Domain.Content;
using PartyGame.Infrastructure.Content;
using Xunit;

namespace PartyGame.Tests.Content;

public sealed class ContentValidationServiceTests
{
    private readonly ContentValidationService _service = new();

    [Fact]
    public void ValidatePackageMetadata_RequiresNamePlAndChecksLengths()
    {
        var emptyNamePack = new GamePackage { NamePl = "" };
        var result1 = _service.ValidatePackageMetadata(emptyNamePack);
        Assert.False(result1.IsValid);
        Assert.Contains(result1.Errors, e => e.Code == "package_name_required");

        var longNamePack = new GamePackage { NamePl = new string('A', 121) };
        var result2 = _service.ValidatePackageMetadata(longNamePack);
        Assert.False(result2.IsValid);
        Assert.Contains(result2.Errors, e => e.Code == "package_name_too_long");
    }

    [Fact]
    public void ValidatePlainText_RejectsHtmlAndScriptTags()
    {
        var xssQuestion = new GameQuestion
        {
            Key = "q_xss",
            TextPl = "<script>alert(1)</script>",
            TextEn = "Normal text",
            Type = QuestionType.PlayerSelection
        };

        var result = _service.ValidateQuestion(xssQuestion, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "text_contains_html");
    }

    [Fact]
    public void ValidateQuestion_ValidatesPlaceholdersForPlayerSelection()
    {
        var invalidPlaceholderQuestion = new GameQuestion
        {
            Key = "q_invalid_ph",
            TextPl = "Gdyby {foo} został szefem?",
            TextEn = "If {player} became boss?",
            Type = QuestionType.PlayerSelection
        };

        var result = _service.ValidateQuestion(invalidPlaceholderQuestion, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "content_invalid_placeholder");
    }

    [Fact]
    public void ValidateQuestion_RequiresMinimumPlayersAtLeastThree()
    {
        var invalidMinPlayers = new GameQuestion
        {
            Key = "q_min",
            TextPl = "Kto jest najmłodszy?",
            TextEn = "Who is youngest?",
            Type = QuestionType.PlayerSelection,
            MinimumPlayers = 2
        };

        var result = _service.ValidateQuestion(invalidMinPlayers, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "invalid_minimum_players");
    }

    [Fact]
    public void ValidateForPublish_ChecksDraftStatusAndActiveContent()
    {
        var publishedPack = new GamePackage
        {
            NamePl = "Pakiet",
            Status = ContentPackageStatus.Published
        };

        var result = _service.ValidateForPublish(publishedPack);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "content_package_already_published");
    }
}
