# Rooms and lobby REST API — Stage 1A

Base path: `/api/rooms`. JSON uses camel case. `phase` is `Lobby`, `Started`, or `Completed`. Every error uses RFC 7807 `ProblemDetails`; field validation uses `ValidationProblemDetails.errors`.

## Public snapshot

`RoomSnapshot` contains `roomCode`, `phase`, server-owned `stateVersion`, `displayConnected`, `minimumPlayers` (3), `maximumPlayers` (10), `canStart`, `settings`, `players`, `createdAtUtc`, and nullable `startedAtUtc`.

Each player contains `id`, `nickname`, `isHost`, `isReady`, `isConnected`, `hasProfilePhoto`, nullable `profilePhotoUrl`, and placeholder `score: 0`. It never contains tokens, token hashes, media paths, or SignalR connection IDs. `settings` contains all fields shown in `contracts/examples/create-room-request.json`.

Clients must only accept a snapshot when its `stateVersion` is newer than the version currently held. A client never creates or increments this value.

## `POST /api/rooms`

Creates a lobby and its host. Body: `{ "nickname": string, "settings"?: RoomSettings }`. Omitting `settings` selects the documented defaults. Returns `201` with `roomCode`, `playerId`, the one-time raw `reconnectToken`, and `snapshot`. Returns `400` for nickname/settings validation and `409` if a unique code cannot be generated.

## `POST /api/rooms/{roomCode}/players`

Body: `{ "nickname": string }`. Returns `201` with the same access DTO as room creation. Returns `400` for an invalid nickname, `404` for an unknown code, and `409` for a duplicate nickname, a full room, or a room outside `Lobby`. Codes are case-insensitive.

## `GET /api/rooms/{roomCode}`

Returns `200` and a public snapshot, or `404`.

## `POST /api/rooms/{roomCode}/players/{playerId}/resume`

Requires `X-Player-Token: <raw reconnect token>`. Returns `200` with `{ "player": PublicPlayer, "snapshot": RoomSnapshot }`. It validates the session but does not mark the player connected. Returns `401` for an invalid/expired token and `404` for an unknown room/player.

## `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo`

Requires `X-Player-Token` and `multipart/form-data` with one `file` field. Accepts non-empty JPEG or PNG up to 5 MiB and checks basic magic bytes. Returns `200` with the updated snapshot; replacement is supported. Returns `400` for invalid files, `401` for an invalid token, and `404` for an unknown room/player. The supplied filename is ignored.

## `GET /api/rooms/{roomCode}/players/{playerId}/profile-photo`

Returns `200` with `image/jpeg` or `image/png` and `Cache-Control: no-store`, or `404`. Internal storage paths are never exposed.

## Example validation error

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "nickname": ["Nickname must contain between 2 and 20 characters after trimming."]
  }
}
```

## Player client order

1. Create or join over REST.
2. Store `playerId` and `reconnectToken` securely.
3. Connect to `/hubs/game` and call `AttachPlayer`.
4. Upload the profile photo.
5. Call `SetReady`.
