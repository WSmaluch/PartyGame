using PartyGame.Domain.Rooms;

namespace PartyGame.Domain.Game;

public sealed class GameQuestionEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }

    public GameQuestionInstance QuestionInstance { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
