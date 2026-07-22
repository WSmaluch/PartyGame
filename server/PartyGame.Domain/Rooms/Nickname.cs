namespace PartyGame.Domain.Rooms;

public static class Nickname
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 20;

    public static string ValidateAndTrim(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinimumLength or > MaximumLength)
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["nickname"] = [$"Nickname must contain between {MinimumLength} and {MaximumLength} characters after trimming."]
            });
        }

        return trimmed;
    }

    public static string Normalize(string value) => value.ToUpperInvariant();
}
