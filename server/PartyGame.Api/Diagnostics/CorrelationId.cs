using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Serilog.Context;

namespace PartyGame.Api.Diagnostics;

public static partial class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";
    public const int MaximumLength = 64;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidValue();

    public static string From(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        return IsValid(supplied) ? supplied! : Create();
    }

    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumLength && ValidValue().IsMatch(value);

    public static string Create() => Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId
        ? traceId
        : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static async Task UseAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = From(context);
        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    public static string ForHub(HubCallerContext context)
    {
        var supplied = context.GetHttpContext()?.Request.Headers[HeaderName].ToString();
        return IsValid(supplied) ? supplied! : Create();
    }
}
