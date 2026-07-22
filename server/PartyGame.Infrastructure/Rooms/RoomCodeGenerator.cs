using System.Security.Cryptography;

namespace PartyGame.Infrastructure.Rooms;

public sealed class RoomCodeGenerator : IRoomCodeGenerator
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 4;
    public const int DefaultMaximumAttempts = 20;
    private readonly Func<int, int> _nextIndex;
    private readonly int _maximumAttempts;

    public RoomCodeGenerator() : this(max => RandomNumberGenerator.GetInt32(max), DefaultMaximumAttempts) { }

    public RoomCodeGenerator(Func<int, int> nextIndex, int maximumAttempts)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);
        _nextIndex = nextIndex;
        _maximumAttempts = maximumAttempts;
    }

    public async Task<string> GenerateAsync(
        Func<string, CancellationToken, Task<bool>> isAvailable,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < _maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = string.Create(CodeLength, _nextIndex, static (buffer, nextIndex) =>
            {
                for (var index = 0; index < buffer.Length; index++)
                {
                    buffer[index] = Alphabet[nextIndex(Alphabet.Length)];
                }
            });

            if (await isAvailable(code, cancellationToken))
            {
                return code;
            }
        }

        throw new RoomCodeGenerationException();
    }
}
