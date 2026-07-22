using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class ArchivedPackageRoomRestartTests
{
    [Fact]
    public async Task ExistingRoom_StartsAfterItsPublishedPackageIsArchived_AndUsesV1Content()
    {
        await using var fixture = await RoomFixture.CreateAsync();
        await fixture.ArchiveV1Async();
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("New", null, null, null, fixture.V1Id))).StatusCode);
        await fixture.ConnectAndStartAsync();
        await fixture.AssertV1PlanAsync();
    }

    [Fact]
    public async Task RealHostRestart_PreservesArchivedRoomBinding_AndReconnectStartsV1Plan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ArchivedPackageRestart", Guid.NewGuid().ToString("N"));
        RoomFixture? first = null;
        try
        {
            first = await RoomFixture.CreateAsync(directory, deleteOnDispose: false);
            await first.ArchiveV1Async();
            var saved = first.Capture();
            await first.DisposeAsync(); first = null;
            await using var second = await RoomFixture.OpenAsync(directory, saved);
            await second.AssertRestartPersistenceAsync();
            await second.ReconnectAndStartAsync();
            await second.AssertV1PlanAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await second.Client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("New", null, null, null, second.V1Id))).StatusCode);
        }
        finally { if (first is not null) await first.DisposeAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class RoomFixture : IAsyncDisposable
    {
        private readonly PartyGameApiFactory _factory; public HttpClient Client { get; }
        public Guid V1Id { get; private set; }
        private Guid _roomId; private string _code = ""; private List<(Guid Id, string Token)> _players = [];
        private RoomFixture(PartyGameApiFactory factory) { _factory = factory; Client = factory.CreateClient(); }
        public static async Task<RoomFixture> CreateAsync(string? directory = null, bool deleteOnDispose = true) { var f = new RoomFixture(new PartyGameApiFactory(directory ?? Path.Combine(Path.GetTempPath(), "PartyGame.ArchivedPackage", Guid.NewGuid().ToString("N")), deleteOnDispose)); await f.SeedAndCreateAsync(); return f; }
        public static async Task<RoomFixture> OpenAsync(string directory, Snapshot s) { var f = new RoomFixture(new PartyGameApiFactory(directory, false)); await f.ReadAsync(s); return f; }
        private async Task SeedAndCreateAsync() { await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var now = DateTimeOffset.UtcNow; var logical = Guid.NewGuid(); var v1 = CreatePackage(logical, 1, "archived_v1", "archived_v1_unique_question", "Pytanie tylko z wersji pierwszej", now); var v2 = CreatePackage(logical, 2, "published_v2", "published_v2_unique_question", "Pytanie tylko z wersji drugiej", now); db.AddRange(v1, v2); await db.SaveChangesAsync(); V1Id = v1.Id; var service = scope.ServiceProvider.GetRequiredService<IRoomService>(); var room = await service.CreateAsync("Host", new RoomSettings { RoundCount = 1, QuestionsPerRound = 4 }, null, ["PlayerSelection"], v1.Id); _roomId = room.Room.Id; _code = room.Room.Code; _players.Add((room.Player.Id, room.ReconnectToken)); for (var i = 0; i < 2; i++) { var joined = await service.JoinAsync(_code, $"P{i}"); _players.Add((joined.Player.Id, joined.ReconnectToken)); } }
        private static GamePackage CreatePackage(Guid logical, int version, string key, string uniqueKey, string text, DateTimeOffset now) { var package = new GamePackage { Id = Guid.NewGuid(), LogicalPackageId = logical, Version = version, Key = key, NamePl = key, NameEn = key, Status = ContentPackageStatus.Published, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, PublishedAtUtc = now }; var category = new GameCategory { Id = Guid.NewGuid(), Package = package, PackageId = package.Id, Key = $"{key}_cat", NamePl = key, NameEn = key, IsActive = true }; for (var i = 0; i < 4; i++) category.Questions.Add(new GameQuestion { Id = Guid.NewGuid(), Category = category, CategoryId = category.Id, Key = i == 0 ? uniqueKey : $"{key}_helper_{i}", Type = QuestionType.PlayerSelection, TextPl = i == 0 ? text : "Pomocnicze", TextEn = "Helper", IsActive = true, MinimumPlayers = 3, CreatedAtUtc = now, UpdatedAtUtc = now }); package.Categories.Add(category); return package; }
        public async Task ArchiveV1Async() { await using var scope = _factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<IRoomService>(); foreach (var player in _players) await service.SetProfilePhotoAsync(_code, player.Id, player.Token, $"p/{player.Id}", "image/png"); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var p = await db.GamePackages.SingleAsync(p => p.Id == V1Id); p.Status = ContentPackageStatus.Archived; p.ArchivedAtUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        public async Task ConnectAndStartAsync() { await using var scope = _factory.Services.CreateAsyncScope(); var s = scope.ServiceProvider.GetRequiredService<IRoomService>(); foreach (var p in _players) { await s.AttachPlayerAsync(_code, p.Id, p.Token); await s.SetProfilePhotoAsync(_code, p.Id, p.Token, $"p/{p.Id}", "image/png"); } await s.AttachDisplayAsync(_code); foreach (var p in _players) await s.SetReadyAsync(_code, p.Id, p.Token, true); }
        public async Task ReconnectAndStartAsync() { await using var scope = _factory.Services.CreateAsyncScope(); var s = scope.ServiceProvider.GetRequiredService<IRoomService>(); foreach (var p in _players) { await s.ResumeAsync(_code, p.Id, p.Token); await s.AttachPlayerAsync(_code, p.Id, p.Token); } await s.AttachDisplayAsync(_code); var room = await s.GetAsync(_code); if (room.Phase == RoomPhase.Lobby) foreach (var p in _players) await s.SetReadyAsync(_code, p.Id, p.Token, true); }
        public async Task AssertV1PlanAsync() { await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var room = await db.GameRooms.Include(r => r.Session!).ThenInclude(s => s.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.Question).SingleAsync(r => r.Id == _roomId); Assert.Equal(V1Id, room.ContentPackageVersionId); Assert.Equal(RoomPhase.Started, room.Phase); var keys = room.Session!.Rounds.SelectMany(r => r.Questions).Select(q => q.Question.Key); Assert.Contains("archived_v1_unique_question", keys); Assert.DoesNotContain("published_v2_unique_question", keys); }
        public Snapshot Capture() => new(V1Id, _roomId, _code, _players);
        private async Task ReadAsync(Snapshot s) { V1Id = s.V1; _roomId = s.Room; _code = s.Code; _players = s.Players; await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); Assert.Equal(ContentPackageStatus.Archived, (await db.GamePackages.SingleAsync(p => p.Id == V1Id)).Status); }
        public async Task AssertRestartPersistenceAsync() { await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var room = await db.GameRooms.Include(r => r.Players).SingleAsync(r => r.Id == _roomId); Assert.Equal(V1Id, room.ContentPackageVersionId); Assert.Equal(_players.Select(p => p.Id).Order(), room.Players.Select(p => p.Id).Order()); Assert.All(room.Players, p => Assert.False(p.IsConnected)); Assert.False(room.DisplayConnected); }
        public ValueTask DisposeAsync() { Client.Dispose(); _factory.Dispose(); return ValueTask.CompletedTask; }
    }
    private sealed record Snapshot(Guid V1, Guid Room, string Code, List<(Guid Id, string Token)> Players);
}
