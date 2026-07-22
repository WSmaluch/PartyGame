namespace PartyGame.Domain.Content;

public sealed class GamePackage
{
    public Guid Id { get; set; }
    public Guid LogicalPackageId { get; set; }
    public int Version { get; set; } = 1;
    public string Key { get; set; } = string.Empty;
    public string NamePl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionPl { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public ContentPackageStatus Status { get; set; } = ContentPackageStatus.Draft;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

    public List<GameCategory> Categories { get; set; } = [];
}
