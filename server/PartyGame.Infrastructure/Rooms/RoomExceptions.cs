namespace PartyGame.Infrastructure.Rooms;

public abstract class RoomException(string message) : Exception(message);
public sealed class RoomNotFoundException : RoomException
{
    public RoomNotFoundException() : base("The room was not found.") { }
}
public sealed class PlayerNotFoundException : RoomException
{
    public PlayerNotFoundException() : base("The player was not found in this room.") { }
}
public sealed class RoomConflictException(string message, string? code = null) : RoomException(message)
{
    /// <summary>Stable machine-readable reason; null keeps the generic conflict code.</summary>
    public string? Code { get; } = code;
}

public static class RoomConflictCodes
{
    public const string RoomNotJoinable = "ROOM_NOT_JOINABLE";
    public const string RoomFull = "ROOM_FULL";
    public const string NicknameTaken = "NICKNAME_TAKEN";
}
public sealed class InvalidPlayerTokenException : RoomException
{
    public InvalidPlayerTokenException() : base("The player token is invalid or expired.") { }
}
public sealed class RoomCodeGenerationException : RoomException
{
    public RoomCodeGenerationException() : base("A unique room code could not be generated.") { }
}
public sealed class PhotoAnswerException(string code, string message) : RoomException(message)
{
    public string Code { get; } = code;
}
public sealed class DrawingAnswerException(string code, string message) : RoomException(message)
{
    public string Code { get; } = code;
}
