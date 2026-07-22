namespace PartyGame.Domain.Content;

public enum QuestionType
{
    PlayerSelection = 0,
    TextAnswer = 1,
    PhotoAnswer = 2,
    DrawingAnswer = 3
}

public sealed class GameQuestion
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Key { get; set; } = string.Empty;
    public QuestionType Type { get; set; } = QuestionType.PlayerSelection;
    public string TextPl { get; set; } = string.Empty;
    public string TextEn { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int MinimumPlayers { get; set; } = 3;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

    public GameCategory Category { get; set; } = null!;
}
