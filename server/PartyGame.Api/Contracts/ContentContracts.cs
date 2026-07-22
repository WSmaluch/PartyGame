namespace PartyGame.Api.Contracts;

public sealed record PackageResponse(
    Guid Id,
    string Key,
    LocalizedText Name,
    LocalizedText Description,
    int CategoryCount,
    int MinimumSupportedRounds,
    int MaximumSupportedRounds,
    bool IsDefault
);

public sealed record LocalizedText(
    string Pl,
    string En
);
