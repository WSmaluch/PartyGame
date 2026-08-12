# PartyGame Web Player (1C)

Web Player to przeglądarkowy klient tego samego PartyGame API i SignalR Hub, co aplikacja iOS. Po wdrożeniu wejście to `http://HOST:PORT/play/`; konfiguracja runtime jest dostępna pod `/play/config.json`.

Użytkownik wpisuje czteroznakowy kod pokoju oraz nick (2–20 znaków). Formularz korzysta z istniejącego `POST /api/rooms/{roomCode}/players`, a następnie łączy się z `/hubs/game` i wywołuje `AttachPlayer`. Parametr `?room=AB12` uzupełnia kod pokoju.

## Lobby, profil i reconnect

Po join Web Player pokazuje Lobby z kodem pokoju, statusem połączenia, listą graczy, avatarami oraz statusami Ready. Przycisk **Gotowy** wywołuje istniejące SignalR `SetReady`; stan z `RoomSnapshotUpdated` jest zawsze źródłem prawdy.

Zdjęcie profilowe jest wysyłane istniejącym `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo` z `X-Player-Token`. Przeglądarka przyjmuje obraz z galerii lub aparatu (`capture="user"`), normalizuje go po stronie klienta do JPEG maksymalnie 1200 px i nie więcej niż 5 MiB. Wybrany obraz jest tylko krótkotrwałym preview; nie trafia do browser storage.

Storage zawiera wyłącznie `roomCode`, `playerId`, `reconnectToken` i `nickname`. Po odświeżeniu klient najpierw używa istniejącego endpointu `resume`, potem `AttachPlayer`; nie tworzy drugiego gracza. Nieważna lub wygasła sesja jest usuwana, a użytkownik wraca kontrolowanie do Join. SignalR używa automatycznego reconnectu i po odzyskaniu transportu ponownie wykonuje `AttachPlayer`. Druga karta z tą samą sesją nie tworzy gracza: backend zastępuje aktywne connection id starszej karty.

## Gameplay: PlayerSelection, tekst i głosowanie

`RoomStarted` oraz każde `RoomSnapshotUpdated` przechodzą przez jeden router oparty wyłącznie na `snapshot.game.stage`. W 1C obsługiwane są `CollectingPlayerSelections`, `CollectingTextAnswers` i `CollectingTextAnswerVotes`; przejście między etapami nigdy nie jest wyliczane lokalnie.

Wybór gracza używa listy zgodnej z klientem iOS (wszyscy pozostali gracze), odpowiedź tekstowa stosuje backendowy limit 150 znaków po trim, a głosowanie korzysta wyłącznie z anonimowych `textResults.votingOptions`. Prywatny event `PlayerPrivateGameStateUpdated` potwierdza odpowiedź tekstową i głos; po refreshu `resume` przywraca ten stan przed pokazaniem formularza.

Każda akcja używa istniejących metod hubu `SubmitPlayerSelectionWithSubmission`, `SubmitTextAnswerWithSubmission` i `SubmitTextAnswerVoteWithSubmission`. Identyfikator `clientSubmissionId` jest stabilny dla tej samej instancji pytania, akcji i karty (`sessionStorage`), więc retry po błędzie sieci nie tworzy drugiej odpowiedzi ani głosu. Timer jest wyłącznie prezentacją deadline serwera; zerowy countdown nie zmienia etapu lokalnie. Sygnalizacja `Ping` koryguje zegar klienta, gdy jest dostępna.

Etapy Photo i Drawing są na razie jawnie nieobsługiwane: klient pokazuje kontrolowany komunikat i nie wysyła fikcyjnych akcji. Results, Round Summary oraz ranking pozostają zakresem kolejnego etapu.

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
