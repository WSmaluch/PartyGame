# Kontrakty SignalR

Wstępne zasady komunikacji:

- klient wysyła intencje użytkownika, a nie gotowe zmiany stanu;
- klient nie nalicza punktów;
- klient nie ustala aktualnego stanu gry;
- każda migawka stanu będzie zawierała rosnące `stateVersion`;
- po ponownym połączeniu klient pobierze aktualną migawkę, zamiast odtwarzać pominięte zdarzenia po swojej stronie.

Metody i zdarzenia lobby Etapu 1A są zdefiniowane w [lobby.md](lobby.md). Komunikaty pytań, rund i finału zostaną dodane w kolejnych etapach.
