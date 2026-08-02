using System.Security.Cryptography;
using System.Text;

namespace PartyGame.Api.Security;

public sealed class OperatorTokenOptions
{
    public const string SectionName = "Security:Operator";
    public string Token { get; set; } = string.Empty;
    public bool IsConfigured => Token.Length >= 32 && !IsPlaceholder(Token);

    private static bool IsPlaceholder(string value) =>
        value.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("change-me", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("example", StringComparison.OrdinalIgnoreCase);

    public bool Matches(string? candidate)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(candidate)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(Token)),
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
    }
}

public sealed class OperatorTokenEndpointFilter(OperatorTokenOptions options, IHostEnvironment environment) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Development without an explicit token keeps the existing local authoring
        // workflow working. Production is guarded at startup and every configured
        // environment uses the same bearer-token boundary.
        if (!options.IsConfigured && environment.IsDevelopment())
            return next(context);

        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !options.Matches(authorization[prefix.Length..]))
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        return next(context);
    }
}
