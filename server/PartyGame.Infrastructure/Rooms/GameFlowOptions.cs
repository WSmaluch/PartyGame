using System.ComponentModel.DataAnnotations;

namespace PartyGame.Infrastructure.Rooms;

public class GameFlowOptions
{
    public const string SectionName = "GameFlow";

    [Range(100, 60000, ErrorMessage = "WorkerIntervalMilliseconds must be between 100 and 60000")]
    public int WorkerIntervalMilliseconds { get; set; } = 1000;

    [Range(0, 30)]
    public int TextAnswerRevealBaseSeconds { get; set; } = 1;

    [Range(0, 10)]
    public int TextAnswerRevealPerAnswerSeconds { get; set; } = 2;

    [Range(0, 60)]
    public int TextAnswerRevealMaximumSeconds { get; set; } = 15;

    [Range(5, 300)]
    public int PhotoAnswerSubmissionSeconds { get; set; } = 90;
    [Range(0, 30)]
    public int PhotoAnswerRevealBaseSeconds { get; set; } = 1;
    [Range(0, 30)]
    public int PhotoAnswerRevealPerPhotoSeconds { get; set; } = 3;
    [Range(0, 120)]
    public int PhotoAnswerRevealMaximumSeconds { get; set; } = 20;
    [Range(1, 60)]
    public int PhotoAnswerResultsSeconds { get; set; } = 10;
    [Range(5, 300)] public int DrawingAnswerSubmissionSeconds { get; set; } = 150;
    [Range(0, 30)] public int DrawingAnswerRevealBaseSeconds { get; set; } = 1;
    [Range(0, 30)] public int DrawingAnswerRevealPerDrawingSeconds { get; set; } = 3;
    [Range(0, 120)] public int DrawingAnswerRevealMaximumSeconds { get; set; } = 20;
    [Range(1, 60)] public int DrawingAnswerResultsSeconds { get; set; } = 10;
}
