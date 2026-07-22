namespace PartyGame.Infrastructure.Persistence;

public sealed class DatabaseMetadata
{
    public int Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
