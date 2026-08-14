namespace PartyGame.Domain.Game;

public enum GameStage
{
    CategoryIntro = 0,
    QuestionIntro = 1,
    CollectingPlayerSelections = 2,
    ShowingQuestionResults = 3,
    RoundSummary = 4,
    GameSummary = 5,
    PausedForDisplay = 6,
    Completed = 7,
    CollectingTextAnswers = 8,
    RevealingTextAnswers = 9,
    CollectingTextAnswerVotes = 10,
    ShowingTextAnswerResults = 11,
    CollectingPhotoAnswers = 12,
    RevealingPhotoAnswers = 13,
    CollectingPhotoAnswerVotes = 14,
    ShowingPhotoAnswerResults = 15,
    CollectingDrawingAnswers = 16,
    RevealingDrawingAnswers = 17,
    CollectingDrawingAnswerVotes = 18,
    ShowingDrawingAnswerResults = 19,
    CollectingFinalSelfies = 20,
    CollectingFinalEdits = 21,
    ShowingFinalPresentation = 22,
    CollectingFinalVotes = 23,
    ShowingFinalResults = 24
}
