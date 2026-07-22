using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Infrastructure.Persistence;

public sealed class EnabledQuestionTypesPersistenceTests
{
    [Fact]
    public async Task Converter_RoundTripsAllThreeTextEnumValues()
    {
        await using var db = CreateDb();
        var room = NewRoom();
        room.EnabledQuestionTypes = [QuestionType.PlayerSelection, QuestionType.TextAnswer, QuestionType.PhotoAnswer, QuestionType.DrawingAnswer];
        db.GameRooms.Add(room);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var restored = await db.GameRooms.SingleAsync();
        Assert.Equal(room.EnabledQuestionTypes, restored.EnabledQuestionTypes);
    }

    [Fact]
    public async Task Comparer_DetectsAddingPhotoAnswer()
    {
        await using var db = CreateDb();
        var room = NewRoom();
        room.EnabledQuestionTypes = [QuestionType.PlayerSelection, QuestionType.TextAnswer];
        db.GameRooms.Add(room);
        await db.SaveChangesAsync();
        room.EnabledQuestionTypes.Add(QuestionType.PhotoAnswer);
        db.ChangeTracker.DetectChanges();
        Assert.True(db.Entry(room).Property(r => r.EnabledQuestionTypes).IsModified);
    }

    private static PartyGameDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options;
        var db = new PartyGameDbContext(options);
        db.Database.OpenConnection();
        db.Database.Migrate();
        return db;
    }

    private static GameRoom NewRoom() => new()
    {
        Id = Guid.NewGuid(),
        Code = "TYPE",
        HostPlayerId = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        Settings = new RoomSettings()
    };
}
