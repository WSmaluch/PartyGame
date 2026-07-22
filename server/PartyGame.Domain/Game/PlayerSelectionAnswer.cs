using PartyGame.Domain.Rooms;

namespace PartyGame.Domain.Game;

public sealed class PlayerSelectionAnswer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid VoterPlayerId { get; set; }
    public Guid SelectedPlayerId { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public int? PointsAwarded { get; set; }

    public GameQuestionInstance QuestionInstance { get; set; } = null!;
    public Player VoterPlayer { get; set; } = null!;
    public Player SelectedPlayer { get; set; } = null!;
}
