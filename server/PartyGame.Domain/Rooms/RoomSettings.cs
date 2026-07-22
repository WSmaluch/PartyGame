namespace PartyGame.Domain.Rooms;

public sealed class RoomSettings
{
    public Guid GameRoomId { get; set; }
    public int RoundCount { get; set; } = 4;
    public int QuestionsPerRound { get; set; } = 5;
    public int PlayerSelectionSeconds { get; set; } = 20;
    public int TextAnswerSeconds { get; set; } = 40;
    public int VotingSeconds { get; set; } = 20;
    public int PhotoSeconds { get; set; } = 45;
    public int DrawingSeconds { get; set; } = 90;
    public int ResultPresentationSeconds { get; set; } = 8;
    public bool FinalRoundEnabled { get; set; } = true;
    public int FinalDrawingPasses { get; set; } = 3;

    public void Validate()
    {
        var errors = new Dictionary<string, string[]>();
        AddRangeError(errors, "roundCount", RoundCount, 1, 10);
        AddRangeError(errors, "questionsPerRound", QuestionsPerRound, 4, 6);
        AddRangeError(errors, "playerSelectionSeconds", PlayerSelectionSeconds, 5, 120);
        AddRangeError(errors, "textAnswerSeconds", TextAnswerSeconds, 5, 180);
        AddRangeError(errors, "votingSeconds", VotingSeconds, 5, 120);
        AddRangeError(errors, "photoSeconds", PhotoSeconds, 10, 180);
        AddRangeError(errors, "drawingSeconds", DrawingSeconds, 30, 300);
        AddRangeError(errors, "resultPresentationSeconds", ResultPresentationSeconds, 3, 30);
        AddRangeError(errors, "finalDrawingPasses", FinalDrawingPasses, 1, 9);
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }
    }

    private static void AddRangeError(Dictionary<string, string[]> errors, string field, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            errors[field] = [$"Value must be between {minimum} and {maximum}."];
        }
    }
}
