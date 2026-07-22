# Interfejs SignalR GameHub

Serwerowy kontrakt SignalR dla trybu PlayerSelection.

## Metody wysyłane przez Klienta
- `AttachPlayer` (Guid playerId, string token) -> Wiąże połączenie WebSocket z danym graczem w konkretnym pokoju.
- `AttachDisplay` () -> Wiąże ekran TV i wznawia grę.
- `SubmitPlayerSelection` (Guid selectedPlayerId) -> Wysyła oddany głos przez gracza. Dostępne tylko podczas etapu `CollectingPlayerSelections`.

## Powiadomienia odbierane przez Klienta
Wszystkie informacje (zarówno dla gracza jak i TV) dostarczane są w jednolitym obiekcie `RoomSnapshot` (który zagnieżdża w sobie `GameSnapshot`). SignalR wysyła paczki pod eventem powiadomienia o nowym Snapshocie za pomocą wywołania grupowego do zapiętych connectionIds (metoda SignalR zdefiniowana jest na froncie jako `.on("StateChanged", (snapshot) => { ... })` lub podobnie wg standardu).
Zawsze należy odrzucić aktualizację ze starszym (mniejszym) `StateVersion`!
