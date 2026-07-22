using Microsoft.EntityFrameworkCore;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Endpoints;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/media/{mediaAssetId:guid}/{variant}", async (Guid mediaAssetId, string variant, PartyGameDbContext db, IMediaStorage storage, CancellationToken cancellationToken) =>
        {
            if (variant is not ("display" or "thumbnail")) return Results.NotFound();
            var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == mediaAssetId, cancellationToken);
            if (asset == null) return Results.NotFound();
            var key = variant == "display" ? asset.DisplayStorageKey : asset.ThumbnailStorageKey;
            var stream = await storage.OpenReadAsync(key, cancellationToken);
            if (stream == null) return Results.NotFound();
            return Results.Stream(stream, asset.ContentType, enableRangeProcessing: true);
        }).WithTags("Media");
        return endpoints;
    }
}
