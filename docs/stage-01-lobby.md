# Etap 1A — lobby

Backend realizuje pełny, serwerowo sterowany przepływ lobby. Host tworzy pokój przez REST i otrzymuje czteroznakowy kod, `playerId` oraz surowy token ponownego połączenia. Kolejne osoby dołączają tym samym kodem. Nickname po przycięciu ma 2–20 znaków i jest unikalny w pokoju bez rozróżniania wielkości liter.

Surowy reconnect token jest generowany kryptograficznie i zwracany tylko w odpowiedzi create/join. SQLite przechowuje wyłącznie SHA-256 tokenu i termin ważności (30 dni). `resume` sprawdza sesję, ale dopiero `AttachPlayer` oznacza gracza jako podłączonego. Token należy przechowywać jak sekret i nie umieszczać w logach.

Każdy gracz przesyła własne zdjęcie JPEG/PNG do 5 MiB, po czym ustawia Ready przez SignalR. Plik otrzymuje nazwę serwera; podstawowe magic bytes są sprawdzane. Kolejny upload atomowo podmienia odwołanie do zdjęcia. Domyślnie pliki deweloperskie są w `${TMPDIR}/PartyGame/media`, poza kodem źródłowym. Ścieżkę można zmienić przez `Media:RootPath`. Dane lokalne czyści się przez zatrzymanie API i usunięcie tego katalogu oraz używanego pliku SQLite.

Display łączy się z `/hubs/game` i wykonuje `AttachDisplay(roomCode)` bez tokenu. W pokoju aktywny jest jeden TV; odświeżona karta zastępuje poprzednią i wywołuje na niej `DisplayReplaced`.

Pokój przechodzi automatycznie z `Lobby` do `Started`, gdy Display jest online, graczy jest 3–10 i wszyscy są połączeni, mają zdjęcie oraz Ready. Blokada per pokój serializuje mutacje, więc `RoomStarted` powstaje dokładnie raz także przy równoczesnych akcjach. `Started` jest w tym etapie stanem końcowym bez pytań, rund, timerów i punktacji.

Każda publiczna zmiana zwiększa serwerowy `stateVersion`; klienci nie wyliczają go i ignorują nie-nowsze migawki. Po restarcie procesu zapisane flagi aktywnych połączeń są zerowane, ponieważ rejestr ConnectionId jest świadomie tylko in-memory.

## Przykładowe wywołania

```bash
curl -sS http://localhost:5050/api/rooms \
  -H 'Content-Type: application/json' \
  -d '{"nickname":"Wojtek"}'

curl -sS http://localhost:5050/api/rooms/7KQX/players \
  -H 'Content-Type: application/json' \
  -d '{"nickname":"Kasia"}'

curl -sS http://localhost:5050/api/rooms/7KQX

curl -sS -X POST http://localhost:5050/api/rooms/7KQX/players/PLAYER_ID/resume \
  -H 'X-Player-Token: RAW_TOKEN'

curl -sS http://localhost:5050/api/rooms/7KQX/players/PLAYER_ID/profile-photo \
  -H 'X-Player-Token: RAW_TOKEN' \
  -F 'file=@profile.jpg;type=image/jpeg'
```

Pełne DTO, odpowiedzi błędów i kolejność metod huba opisują `contracts/api/rooms.md` oraz `contracts/signalr/lobby.md`.

Automatyczny scenariusz demonstracyjny (trzech graczy, Display, zdjęcia, równoczesne Ready, pojedynczy start, rozłączenie i reconnect) uruchamia `bash scripts/demo-lobby.sh`.

## Ograniczenia etapu

Nie ma jeszcze pytań, kategorii, rund, odpowiedzi, głosowania, punktacji, finału, panelu admina ani pełnego wznowienia trwającej gry po restarcie. Stan `Started` jedynie potwierdza spełnienie warunków lobby. Transport HTTP jest przeznaczony do developmentu w zaufanej sieci; wdrożenie wymaga HTTPS.
