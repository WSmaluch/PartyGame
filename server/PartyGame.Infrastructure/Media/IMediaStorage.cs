namespace PartyGame.Infrastructure.Media;

public interface IMediaStorage
{
    Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default);
    Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default);
    Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record ProfilePhotoMediaWriteRequest(
    Guid MediaAssetId,
    Guid RoomId,
    Guid PlayerId,
    Stream Content,
    long ByteLength,
    string ContentType);

public sealed record PhotoMediaWriteRequest(
    Guid RoomId,
    Guid QuestionInstanceId,
    Guid PhotoAnswerId,
    Stream Content,
    long ByteLength,
    string ContentType);

public sealed record DrawingMediaWriteRequest(
    Guid RoomId,
    Guid QuestionInstanceId,
    Guid DrawingAnswerId,
    Stream Content,
    long ByteLength,
    string ContentType);

public sealed record StoredMediaResult(
    string DisplayStorageKey,
    string ThumbnailStorageKey,
    int Width,
    int Height,
    long ByteLength,
    string Sha256,
    string ContentType = "image/jpeg");

public sealed class PhotoMediaException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
