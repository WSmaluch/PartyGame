using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class ProfilePhotoCleanupTests
{
    [Fact]
    public async Task ReplaceThenRestart_RetriesFailedCleanupWithoutChangingActivePhoto()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfilePhotoCleanup.Api", Guid.NewGuid().ToString("N"));
        var mediaRoot = Path.Combine(directory, "media");
        var storage = new FailingDeleteStorage(new LocalMediaStorage(Options.Create(new MediaOptions { RootPath = mediaRoot })));
        Guid oldAssetId;
        Guid activeAssetId;
        RoomAccessResponse access;

        try
        {
            using (var hostA = CreateFactory(directory, storage))
            {
                var client = hostA.CreateClient();
                access = await CreateRoomAsync(client);
                Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync())).StatusCode);
                oldAssetId = await ActiveAssetIdAsync(hostA, access.PlayerId);

                storage.FailDeletes = true;
                Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync(png: true), "image/png")).StatusCode);
                activeAssetId = await ActiveAssetIdAsync(hostA, access.PlayerId);

                Assert.NotEqual(oldAssetId, activeAssetId);
                Assert.NotNull(await FindAssetAsync(hostA, oldAssetId));
                Assert.NotNull(await FindAssetAsync(hostA, activeAssetId));
            }

            using var hostB = new PartyGameApiFactory(directory, deleteOnDispose: false);
            var restartedClient = hostB.CreateClient();
            Assert.Null(await FindAssetAsync(hostB, oldAssetId));
            var active = await FindAssetAsync(hostB, activeAssetId);
            Assert.NotNull(active);
            var response = await restartedClient.GetAsync($"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentReplacements_KeepTheLastSuccessfulAssetAndRemoveTheSupersededOne()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfilePhotoCleanup.Concurrent", Guid.NewGuid().ToString("N"));
        var mediaRoot = Path.Combine(directory, "media");
        var storage = new GatedProfilePhotoStorage(new LocalMediaStorage(Options.Create(new MediaOptions { RootPath = mediaRoot })), gateOnSave: 2);

        try
        {
            using var factory = CreateFactory(directory, storage);
            var client = factory.CreateClient();
            var concurrentClient = factory.CreateClient();
            var access = await CreateRoomAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync())).StatusCode);

            var firstUpload = UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync(png: true), "image/png");
            await storage.FirstSaveEntered.Task;

            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(concurrentClient, access, await PhotoAnswerTestHarness.ImageAsync(png: true), "image/png")).StatusCode);
            var secondAssetId = await ActiveAssetIdAsync(factory, access.PlayerId);

            storage.ReleaseFirstSave();
            Assert.Equal(HttpStatusCode.OK, (await firstUpload).StatusCode);
            var finalAssetId = await ActiveAssetIdAsync(factory, access.PlayerId);

            Assert.NotEqual(secondAssetId, finalAssetId);
            Assert.NotNull(await FindAssetAsync(factory, finalAssetId));
            Assert.Null(await FindAssetAsync(factory, secondAssetId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Replace_CommitsTheNewPointerBeforeDeletingTheOldVariants()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfilePhotoCleanup.Order", Guid.NewGuid().ToString("N"));
        var mediaRoot = Path.Combine(directory, "media");
        var storage = new InspectingDeleteStorage(new LocalMediaStorage(Options.Create(new MediaOptions { RootPath = mediaRoot })));

        try
        {
            using var factory = CreateFactory(directory, storage);
            var client = factory.CreateClient();
            var access = await CreateRoomAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync())).StatusCode);
            var oldAssetId = await ActiveAssetIdAsync(factory, access.PlayerId);
            storage.IsOldAssetActiveAsync = () => IsAssetActiveAsync(factory, access.PlayerId, oldAssetId);

            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync(png: true), "image/png")).StatusCode);

            Assert.True(storage.DeleteObserved);
            Assert.False(storage.OldAssetWasActiveWhenDeleteStarted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Replace_RemainsSuccessfulWhenCleanupServiceThrowsAfterCommit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfilePhotoCleanup.Throwing", Guid.NewGuid().ToString("N"));

        try
        {
            using var factory = new PartyGameApiFactory(directory, deleteOnDispose: false, configureServices: services =>
            {
                services.RemoveAll<IProfilePhotoCleanupService>();
                services.AddSingleton<IProfilePhotoCleanupService, ThrowingProfilePhotoCleanupService>();
            });
            var client = factory.CreateClient();
            var access = await CreateRoomAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync())).StatusCode);
            var oldAssetId = await ActiveAssetIdAsync(factory, access.PlayerId);

            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, access, await PhotoAnswerTestHarness.ImageAsync(png: true), "image/png")).StatusCode);
            var activeAssetId = await ActiveAssetIdAsync(factory, access.PlayerId);

            Assert.NotEqual(oldAssetId, activeAssetId);
            Assert.NotNull(await FindAssetAsync(factory, oldAssetId));
            Assert.NotNull(await FindAssetAsync(factory, activeAssetId));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo")).StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static PartyGameApiFactory CreateFactory(string directory, IMediaStorage storage) =>
        new(directory, deleteOnDispose: false, configureServices: services =>
        {
            services.RemoveAll<IMediaStorage>();
            services.AddSingleton(storage);
        });

    private static async Task<RoomAccessResponse> CreateRoomAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("Cleanup host", null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, RoomAccessResponse access, byte[] bytes, string contentType = "image/jpeg")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
        request.Headers.Add("X-Player-Token", access.ReconnectToken);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Content = new MultipartFormDataContent { { content, "file", contentType == "image/png" ? "profile.png" : "profile.jpg" } };
        return await client.SendAsync(request);
    }

    private static async Task<Guid> ActiveAssetIdAsync(PartyGameApiFactory factory, Guid playerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Players
            .Where(player => player.Id == playerId)
            .Select(player => player.ProfilePhotoMediaAssetId)
            .SingleAsync())!.Value;
    }

    private static async Task<bool> IsAssetActiveAsync(PartyGameApiFactory factory, Guid playerId, Guid assetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Players
            .AnyAsync(player => player.Id == playerId && player.ProfilePhotoMediaAssetId == assetId);
    }

    private static async Task<MediaAsset?> FindAssetAsync(PartyGameApiFactory factory, Guid assetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().MediaAssets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed class FailingDeleteStorage(IMediaStorage inner) : IMediaStorage
    {
        public bool FailDeletes { get; set; }
        public Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveProfilePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SavePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveDrawingAsync(request, cancellationToken);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => inner.OpenReadAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            FailDeletes ? throw new IOException("Injected deletion failure.") : inner.DeleteAsync(storageKey, cancellationToken);
    }

    private sealed class GatedProfilePhotoStorage(IMediaStorage inner, int gateOnSave) : IMediaStorage
    {
        private int profileSaveCount;
        private readonly TaskCompletionSource firstSaveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref profileSaveCount) == gateOnSave)
            {
                FirstSaveEntered.TrySetResult();
                await firstSaveRelease.Task.WaitAsync(cancellationToken);
            }
            return await inner.SaveProfilePhotoAsync(request, cancellationToken);
        }

        public void ReleaseFirstSave() => firstSaveRelease.TrySetResult();
        public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SavePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveDrawingAsync(request, cancellationToken);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => inner.OpenReadAsync(storageKey, cancellationToken);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => inner.DeleteAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);
    }

    private sealed class InspectingDeleteStorage(IMediaStorage inner) : IMediaStorage
    {
        public Func<Task<bool>>? IsOldAssetActiveAsync { get; set; }
        public bool DeleteObserved { get; private set; }
        public bool OldAssetWasActiveWhenDeleteStarted { get; private set; }

        public Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveProfilePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SavePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveDrawingAsync(request, cancellationToken);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => inner.OpenReadAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);

        public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (!DeleteObserved && IsOldAssetActiveAsync is not null)
            {
                OldAssetWasActiveWhenDeleteStarted = await IsOldAssetActiveAsync();
                DeleteObserved = true;
            }

            await inner.DeleteAsync(storageKey, cancellationToken);
        }
    }

    private sealed class ThrowingProfilePhotoCleanupService : IProfilePhotoCleanupService
    {
        public Task<bool> CleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken = default) =>
            throw new IOException("Injected cleanup failure.");

        public Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
