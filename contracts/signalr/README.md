# Kontrakty SignalR

Wstępne zasady komunikacji:

- klient wysyła intencje użytkownika, a nie gotowe zmiany stanu;
- klient nie nalicza punktów;
- klient nie ustala aktualnego stanu gry;
- każda migawka stanu będzie zawierała rosnące `stateVersion`;
- po ponownym połączeniu klient pobierze aktualną migawkę, zamiast odtwarzać pominięte zdarzenia po swojej stronie.

Kontrakty: [lobby](lobby.md), [PlayerSelection](player-selection-game.md), [PhotoAnswer](photo-answer-game.md) i [DrawingAnswer](drawing-answer-game.md). Pełny kształt snapshotu gry opisuje [game-snapshot.md](../api/game-snapshot.md), a prywatny stan gracza [player-private-game-state.md](../api/player-private-game-state.md).
