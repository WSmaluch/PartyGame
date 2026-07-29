using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PartyGame.Api.Contracts;
using PartyGame.Api.Hubs;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Api.Endpoints;

public static class RoomEndpoints
{
    private const string PlayerTokenHeader = "X-Player-Token";

    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var rooms = endpoints.MapGroup("/api/rooms").WithTags("Rooms");

        rooms.MapPost("/", async (CreateRoomRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            var result = await roomService.CreateAsync(request.Nickname, request.Settings?.ToDomain(), request.SelectedPackageKeys, request.EnabledQuestionTypes, request.ContentPackageVersionId, cancellationToken);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(result.Room.Code, result.Player.Id, cancellationToken);
            return Results.Created($"/api/rooms/{result.Room.Code}",
                new RoomAccessResponse(result.Room.Code, result.Player.Id, result.ReconnectToken, result.Room.ToSnapshot(), privateState));
        });

        rooms.MapPost("/{roomCode}/players", async (string roomCode, JoinRoomRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            var result = await roomService.JoinAsync(roomCode, request.Nickname, cancellationToken);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(result.Room.Code, result.Player.Id, cancellationToken);
            return Results.Created($"/api/rooms/{result.Room.Code}/players/{result.Player.Id}",
                new RoomAccessResponse(result.Room.Code, result.Player.Id, result.ReconnectToken, result.Room.ToSnapshot(), privateState));
        });

        rooms.MapGet("/{roomCode}", async (string roomCode, IRoomService roomService, CancellationToken cancellationToken) =>
            Results.Ok((await roomService.GetAsync(roomCode, cancellationToken)).ToSnapshot()));

        // Player-authorized operational diagnostics. It deliberately exposes only
        // hashes and aggregate counts; reconnect tokens and media bytes never leave
        // storage through this endpoint.
        rooms.MapGet("/{roomCode}/submission-audit", async (
            string roomCode, Guid playerId, string reconnectToken, IRoomService roomService,
            PartyGame.Infrastructure.Persistence.PartyGameDbContext db, CancellationToken cancellationToken) =>
        {
            var authorization = await roomService.ResumeAsync(roomCode, playerId, reconnectToken, cancellationToken);
            var entries = await db.SubmissionAuditEntries.AsNoTracking()
                .Where(entry => entry.RoomId == authorization.Room.Id)
                .Select(entry => new { entry.PlayerId, entry.QuestionInstanceId, actionType = entry.ActionType.ToString(), clientSubmissionId = entry.ClientSubmissionId, payloadFingerprint = entry.PayloadFingerprint, result = entry.Result.ToString(), entry.CreatedAtUtc })
                .ToListAsync(cancellationToken);
            var orderedEntries = entries.OrderBy(entry => entry.CreatedAtUtc).ToArray();
            return Results.Ok(new { entries = orderedEntries });
        });

        rooms.MapPost("/{roomCode}/players/{playerId:guid}/resume", async (
            string roomCode,
            Guid playerId,
            HttpRequest request,
            IRoomService roomService,
            CancellationToken cancellationToken) =>
        {
            var result = await roomService.ResumeAsync(roomCode, playerId, ReadToken(request), cancellationToken);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, cancellationToken);
            return Results.Ok(new ResumePlayerResponse(result.Player.ToPublic(result.Room.Code), result.Room.ToSnapshot(), privateState));
        });

        rooms.MapPost("/{roomCode}/players/{playerId:guid}/profile-photo", async (
            string roomCode,
            Guid playerId,
            HttpRequest request,
            IFormFile file,
            IRoomService roomService,
            IMediaStorage storage,
            RoomNotifier notifier,
            CancellationToken cancellationToken) =>
        {
            var authorization = await roomService.ResumeAsync(roomCode, playerId, ReadToken(request), cancellationToken);
            var mediaAssetId = Guid.NewGuid();
            await using var stream = file.OpenReadStream();
            StoredMediaResult stored;
            try
            {
                stored = await storage.SaveProfilePhotoAsync(new ProfilePhotoMediaWriteRequest(
                    mediaAssetId,
                    authorization.Room.Id,
                    authorization.Player.Id,
                    stream,
                    file.Length,
                    file.ContentType), cancellationToken);
            }
            catch (PhotoMediaException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
            }

            RoomMutationResult result;
            try
            {
                result = await roomService.SetProfilePhotoAsync(
                    authorization.Room.Code,
                    playerId,
                    ReadToken(request),
                    mediaAssetId,
                    stored,
                    cancellationToken);
            }
            catch
            {
                await storage.DeleteAsync(stored.DisplayStorageKey, CancellationToken.None);
                await storage.DeleteAsync(stored.ThumbnailStorageKey, CancellationToken.None);
                throw;
            }
            await notifier.NotifyAsync(result, cancellationToken);
            return Results.Ok(result.Room.ToSnapshot());
        })
        .DisableAntiforgery();

        rooms.MapGet("/{roomCode}/players/{playerId:guid}/profile-photo", async (
            string roomCode,
            Guid playerId,
            HttpResponse response,
            IRoomService roomService,
            PartyGame.Infrastructure.Persistence.PartyGameDbContext db,
            IMediaStorage storage,
            CancellationToken cancellationToken) =>
        {
            var room = await roomService.GetAsync(roomCode, cancellationToken);
            var player = room.Players.SingleOrDefault(candidate => candidate.Id == playerId);
            if (player is null || !player.HasProfilePhoto || player.ProfilePhotoMediaAssetId is null)
            {
                return Results.NotFound();
            }
            var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(asset =>
                asset.Id == player.ProfilePhotoMediaAssetId &&
                asset.MediaKind == PartyGame.Domain.Game.MediaKind.ProfilePhoto &&
                asset.RoomId == room.Id &&
                asset.PlayerId == player.Id,
                cancellationToken);
            if (asset is null) return Results.NotFound();
            var stream = await storage.OpenReadAsync(asset.DisplayStorageKey, cancellationToken);
            if (stream is null)
            {
                return Results.NotFound();
            }
            response.Headers.CacheControl = "no-store";
            response.ContentLength = stream.Length;
            return Results.Stream(stream, asset.ContentType, enableRangeProcessing: true);
        });

        rooms.MapPost("/{roomCode}/questions/{questionInstanceId:guid}/photo-answers", async (
            string roomCode,
            Guid questionInstanceId,
            [FromForm] Guid playerId,
            [FromForm] string reconnectToken,
            [FromForm] Guid clientSubmissionId,
            IFormFile? photo,
            IRoomService roomService,
            RoomNotifier notifier,
            IRoomConnectionRegistry connections,
            IHubContext<GameHub> hubContext,
            CancellationToken cancellationToken) =>
        {
            if (photo is null) throw new PhotoAnswerException("photo_answer_file_missing", "A photo file is required.");
            await using var content = photo.OpenReadStream();
            var result = await roomService.SubmitPhotoAnswerAsync(roomCode, playerId, reconnectToken, questionInstanceId, clientSubmissionId, content, photo.Length, photo.ContentType, cancellationToken);
            var mutation = new RoomMutationResult(result.Room, result.Created, false);
            await notifier.NotifyAsync(mutation, cancellationToken);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, cancellationToken);
            var connectionId = connections.GetActivePlayerConnection(playerId);
            if (connectionId != null)
                await hubContext.Clients.Client(connectionId).SendAsync("PlayerPrivateGameStateUpdated", privateState, cancellationToken);
            return Results.Ok(new PhotoAnswerUploadResponse(result.PhotoAnswerId, privateState, result.Room.ToSnapshot()));
        }).DisableAntiforgery();

        rooms.MapPost("/{roomCode}/questions/{questionInstanceId:guid}/drawing-answers", async (string roomCode, Guid questionInstanceId, [FromForm] Guid playerId, [FromForm] string reconnectToken, [FromForm] Guid clientSubmissionId, IFormFile? drawing, IRoomService roomService, RoomNotifier notifier, IRoomConnectionRegistry connections, IHubContext<GameHub> hubContext, CancellationToken cancellationToken) =>
        {
            if (drawing is null) throw new DrawingAnswerException("drawing_answer_file_missing", "A drawing file is required.");
            await using var content = drawing.OpenReadStream();
            var result = await roomService.SubmitDrawingAnswerAsync(roomCode, playerId, reconnectToken, questionInstanceId, clientSubmissionId, content, drawing.Length, drawing.ContentType, cancellationToken);
            await notifier.NotifyAsync(new RoomMutationResult(result.Room, result.Created, false), cancellationToken);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, cancellationToken); var connectionId = connections.GetActivePlayerConnection(playerId);
            if (connectionId != null) await hubContext.Clients.Client(connectionId).SendAsync("PlayerPrivateGameStateUpdated", privateState, cancellationToken);
            return Results.Ok(new DrawingAnswerUploadResponse(result.DrawingAnswerId, privateState, result.Room.ToSnapshot()));
        }).DisableAntiforgery();

        return endpoints;
    }

    private static string? ReadToken(HttpRequest request) => request.Headers[PlayerTokenHeader].FirstOrDefault();

}

public sealed record PhotoAnswerUploadResponse(Guid PhotoAnswerId, PlayerPrivateGameState PlayerPrivateGameState, RoomSnapshot RoomSnapshot);
public sealed record DrawingAnswerUploadResponse(Guid DrawingAnswerId, PlayerPrivateGameState PlayerPrivateGameState, RoomSnapshot RoomSnapshot);
