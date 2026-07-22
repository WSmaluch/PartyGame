using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PartyGame.Api.Contracts;
using PartyGame.Api.Hubs;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Api.Endpoints;

public static class RoomEndpoints
{
    private const long MaximumPhotoBytes = 5 * 1024 * 1024;
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
            IProfilePhotoStorage storage,
            RoomNotifier notifier,
            CancellationToken cancellationToken) =>
        {
            var authorization = await roomService.ResumeAsync(roomCode, playerId, ReadToken(request), cancellationToken);
            var errors = ValidatePhoto(file);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            await using var stream = file.OpenReadStream();
            if (!await HasValidSignatureAsync(stream, file.ContentType, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["The file contents do not match the declared JPEG or PNG type."]
                });
            }
            stream.Position = 0;
            var previousStorageKey = authorization.Player.ProfilePhotoStorageKey;
            var storageKey = await storage.SaveAsync(
                authorization.Room.Code,
                playerId,
                stream,
                file.ContentType,
                cancellationToken);
            RoomMutationResult result;
            try
            {
                result = await roomService.SetProfilePhotoAsync(
                    authorization.Room.Code,
                    playerId,
                    ReadToken(request),
                    storageKey,
                    file.ContentType,
                    cancellationToken);
            }
            catch
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
                throw;
            }
            if (previousStorageKey is not null)
            {
                await storage.DeleteAsync(previousStorageKey, CancellationToken.None);
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
            IProfilePhotoStorage storage,
            CancellationToken cancellationToken) =>
        {
            var room = await roomService.GetAsync(roomCode, cancellationToken);
            var player = room.Players.SingleOrDefault(candidate => candidate.Id == playerId);
            if (player is null || !player.HasProfilePhoto || player.ProfilePhotoStorageKey is null || player.ProfilePhotoContentType is null)
            {
                return Results.NotFound();
            }
            var stream = await storage.OpenReadAsync(player.ProfilePhotoStorageKey, cancellationToken);
            if (stream is null)
            {
                return Results.NotFound();
            }
            response.Headers.CacheControl = "no-store";
            return Results.Stream(stream, player.ProfilePhotoContentType);
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

    private static Dictionary<string, string[]> ValidatePhoto(IFormFile file)
    {
        var errors = new Dictionary<string, string[]>();
        if (file.Length == 0)
        {
            errors["file"] = ["The profile photo cannot be empty."];
        }
        else if (file.Length > MaximumPhotoBytes)
        {
            errors["file"] = ["The profile photo cannot exceed 5 MB."];
        }
        else if (file.ContentType is not ("image/jpeg" or "image/png"))
        {
            errors["file"] = ["Only JPEG and PNG profile photos are accepted."];
        }
        return errors;
    }

    private static async Task<bool> HasValidSignatureAsync(Stream stream, string contentType, CancellationToken cancellationToken)
    {
        var signature = new byte[8];
        var read = await stream.ReadAsync(signature, cancellationToken);
        return contentType switch
        {
            "image/jpeg" => read >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff,
            "image/png" => read == 8 && signature.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            _ => false
        };
    }
}

public sealed record PhotoAnswerUploadResponse(Guid PhotoAnswerId, PlayerPrivateGameState PlayerPrivateGameState, RoomSnapshot RoomSnapshot);
public sealed record DrawingAnswerUploadResponse(Guid DrawingAnswerId, PlayerPrivateGameState PlayerPrivateGameState, RoomSnapshot RoomSnapshot);
