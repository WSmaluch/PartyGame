using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PartyGame.Infrastructure.Persistence;

public sealed class PartyGameDbContextFactory : IDesignTimeDbContextFactory<PartyGameDbContext>
{
    public PartyGameDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARTYGAME_CONNECTION_STRING")
            ?? "Data Source=partygame.db";
        var options = new DbContextOptionsBuilder<PartyGameDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new PartyGameDbContext(options);
    }
}
