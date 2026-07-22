namespace PartyGame.Domain.Content;

public sealed class GameCategory
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NamePl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionPl { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

    public GamePackage Package { get; set; } = null!;
    public List<GameQuestion> Questions { get; set; } = [];
}
