using Microsoft.EntityFrameworkCore;
using PartyGame.Api.Contracts;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Endpoints;

public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var content = endpoints.MapGroup("/api/content").WithTags("Content");

        content.MapGet("/packages", async (PartyGameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var packages = await dbContext.GamePackages
                .Where(p => p.IsActive)
                .Select(p => new PackageResponse(
                    p.Id,
                    p.Key,
                    new LocalizedText(p.NamePl, p.NameEn),
                    new LocalizedText(p.DescriptionPl, p.DescriptionEn),
                    p.Categories.Count(c => c.IsActive),
                    p.Categories.Count(c => c.IsActive && c.Questions.Count(q => q.IsActive) >= 4) > 0 ? 1 : 0, // Minimum Supported Rounds roughly mapped
                    p.Categories.Count(c => c.IsActive && c.Questions.Count(q => q.IsActive) >= 4),             // Maximum Supported Rounds roughly mapped
                    p.IsDefault
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(packages);
        });

        return endpoints;
    }
}
