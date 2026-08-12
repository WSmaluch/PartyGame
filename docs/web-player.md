# PartyGame Web Player (1B)

Web Player to przeglądarkowy klient tego samego PartyGame API i SignalR Hub, co aplikacja iOS. Po wdrożeniu wejście to `http://HOST:PORT/play/`; konfiguracja runtime jest dostępna pod `/play/config.json`.

Użytkownik wpisuje czteroznakowy kod pokoju oraz nick (2–20 znaków). Formularz korzysta z istniejącego `POST /api/rooms/{roomCode}/players`, a następnie łączy się z `/hubs/game` i wywołuje `AttachPlayer`. Parametr `?room=AB12` uzupełnia kod pokoju.

## Lobby, profil i reconnect

Po join Web Player pokazuje Lobby z kodem pokoju, statusem połączenia, listą graczy, avatarami oraz statusami Ready. Przycisk **Gotowy** wywołuje istniejące SignalR `SetReady`; stan z `RoomSnapshotUpdated` jest zawsze źródłem prawdy.

Zdjęcie profilowe jest wysyłane istniejącym `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo` z `X-Player-Token`. Przeglądarka przyjmuje obraz z galerii lub aparatu (`capture="user"`), normalizuje go po stronie klienta do JPEG maksymalnie 1200 px i nie więcej niż 5 MiB. Wybrany obraz jest tylko krótkotrwałym preview; nie trafia do browser storage.

Storage zawiera wyłącznie `roomCode`, `playerId`, `reconnectToken` i `nickname`. Po odświeżeniu klient najpierw używa istniejącego endpointu `resume`, potem `AttachPlayer`; nie tworzy drugiego gracza. Nieważna lub wygasła sesja jest usuwana, a użytkownik wraca kontrolowanie do Join. SignalR używa automatycznego reconnectu i po odzyskaniu transportu ponownie wykonuje `AttachPlayer`. Druga karta z tą samą sesją nie tworzy gracza: backend zastępuje aktywne connection id starszej karty.

Gdy backend wyśle `RoomStarted`, aplikacja pokazuje bezpieczny ekran przejściowy. Pełna obsługa pytań, odpowiedzi, głosowania, wyników i rankingu nadal nie jest zaimplementowana w przeglądarce.

Lokalny development:

```bash
cd apps/player-web
npm ci
npm run dev
npm test
npm run lint
npm run build
```

Skopiuj `.env.example` do `.env.local`, aby wskazać lokalny backend. Dla release nie edytuj źródeł: `/play/config.json` jest tworzony przez deployment i używa same-origin URL-i.
