namespace PartyGame.Infrastructure.Rooms;

public interface IPlayerSessionService
{
    string GenerateToken();
    string HashToken(string token);
    bool VerifyToken(string token, string expectedHash);
}
