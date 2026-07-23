using Microsoft.Extensions.Diagnostics.HealthChecks;
using PartyGame.Infrastructure.Media;

namespace PartyGame.Api.Health;

public sealed class MediaStorageHealthCheck(
    IMediaStorageDiagnosticsService diagnostics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await diagnostics.GetAsync(cancellationToken);
        return result.Status switch
        {
            MediaStorageDiagnosticStatus.Healthy => HealthCheckResult.Healthy("Local media storage is healthy."),
            MediaStorageDiagnosticStatus.Degraded => HealthCheckResult.Degraded("Local media storage has low free capacity."),
            MediaStorageDiagnosticStatus.NotSupported => HealthCheckResult.Unhealthy("Media storage diagnostics are not supported."),
            _ => HealthCheckResult.Unhealthy("Local media storage is unavailable.")
        };
    }
}
