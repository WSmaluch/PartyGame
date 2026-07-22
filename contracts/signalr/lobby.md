# Lobby SignalR contract — Stage 1A

Hub path: `/hubs/game`. Existing `Ping(): { status, utcTime }` remains supported.

## Client → server methods

- `AttachPlayer(roomCode: string, playerId: UUID, reconnectToken: string): RoomSnapshot` validates the session, replaces an older connection for the same player, joins the room group, and marks the player connected.
- `AttachDisplay(roomCode: string): RoomSnapshot` needs no token. It replaces an older display connection, joins the room group, and marks the required TV connected.
- `SetReady(roomCode: string, playerId: UUID, reconnectToken: string, isReady: boolean): RoomSnapshot` is lobby-only. Setting `true` requires a profile photo.
- `GetRoomSnapshot(roomCode: string): RoomSnapshot` returns the current public state.

Invalid hub operations complete with a `HubException`; no reconnect token is included in its message.

## Server → client events

- `RoomSnapshotUpdated(RoomSnapshot)` sends a full authoritative snapshot after a public change.
- `RoomStarted(RoomSnapshot)` is sent exactly once for the atomic `Lobby → Started` transition.
- `DisplayReplaced()` is sent to the former display when a new display attaches.

The `stateVersion` in every snapshot is generated only by the server. Clients should discard lower or equal versions. Full snapshots, not patches, are the Stage 1A synchronization format.

## Connection lifecycle

Player: create/join REST → securely persist credentials → connect hub → `AttachPlayer` → upload photo → `SetReady`. A reconnect repeats the hub connection and `AttachPlayer` using the same player ID/token; calling `resume` first is optional.

Display: connect hub → `AttachDisplay(roomCode)` → listen to `RoomSnapshotUpdated`, `RoomStarted`, and `DisplayReplaced`.

Only the newest connection for a player or display is active. A late disconnect from a replaced connection cannot mark its replacement offline. Actual disconnects update the public snapshot.

## Automatic start

The server starts a room only when it is still in `Lobby`, the display is connected, there are 3–10 players, and every player is connected, has a profile photo, and is ready. There is no manual start method.
