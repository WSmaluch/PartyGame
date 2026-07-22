using System.Collections.Concurrent;

namespace PartyGame.Infrastructure.Content;

/// <summary>
/// Serializes lifecycle operations for one logical package/version in this API process.
/// The database indexes remain the cross-process source of truth.
/// </summary>
public sealed class ContentPackageLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim ForLogicalPackage(Guid logicalPackageId) => For($"logical:{logicalPackageId:N}");

    public SemaphoreSlim ForVersion(Guid packageVersionId) => For($"version:{packageVersionId:N}");

    private SemaphoreSlim For(string key) => _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
}
