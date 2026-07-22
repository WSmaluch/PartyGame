namespace PartyGame.Infrastructure.Rooms;

public interface IRoomCodeGenerator
{
    Task<string> GenerateAsync(
        Func<string, CancellationToken, Task<bool>> isAvailable,
        CancellationToken cancellationToken = default);
}
