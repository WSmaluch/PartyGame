using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace PartyGame.Infrastructure.Media;

public class LocalMediaStorageProbe(
    ILogger<LocalMediaStorageProbe> logger) : IMediaStorageProbe
{
    public async Task<bool> RunAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var probeKey = $".diagnostics/{Guid.NewGuid():N}.probe";
        string? probePath = null;
        try
        {
            Directory.CreateDirectory(rootPath);
            if (IsReparsePoint(rootPath))
                throw new IOException("The diagnostics root cannot be a symbolic link.");

            var diagnosticsPath = MediaStoragePathResolver.ResolveStoragePath(rootPath, ".diagnostics");
            Directory.CreateDirectory(diagnosticsPath);
            if (IsReparsePoint(diagnosticsPath))
                throw new IOException("The diagnostics directory cannot be a symbolic link.");

            probePath = MediaStoragePathResolver.ResolveStoragePath(rootPath, probeKey);
            var expected = RandomNumberGenerator.GetBytes(32);
            await using (var write = new FileStream(
                             probePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            {
                await write.WriteAsync(expected, cancellationToken);
            }

            var actual = await ReadProbeFileAsync(probePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new IOException("The diagnostics probe content did not match.");

            File.Delete(probePath);
            probePath = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(
                "Media storage diagnostics probe failed; error type {ErrorType}",
                exception.GetType().Name);
            return false;
        }
        finally
        {
            if (probePath is not null)
            {
                try
                {
                    if (File.Exists(probePath) && !IsReparsePoint(probePath))
                        File.Delete(probePath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        "Media storage diagnostics probe cleanup failed; error type {ErrorType}",
                        exception.GetType().Name);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    protected virtual Task<byte[]> ReadProbeFileAsync(
        string probePath,
        CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(probePath, cancellationToken);
}
