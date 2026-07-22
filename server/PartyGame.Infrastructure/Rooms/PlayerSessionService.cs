using System.Security.Cryptography;
using System.Text;

namespace PartyGame.Infrastructure.Rooms;

public sealed class PlayerSessionService : IPlayerSessionService
{
    public string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool VerifyToken(string token, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || expectedHash.Length != 64)
        {
            return false;
        }

        try
        {
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expected = Convert.FromHexString(expectedHash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
