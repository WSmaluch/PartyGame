# PartyGame Web Player (1D)

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

## Gameplay: Photo, Drawing i media voting

Etap 1D obsługuje `CollectingPhotoAnswers`, `CollectingDrawingAnswers`, `CollectingPhotoAnswerVotes` i `CollectingDrawingAnswerVotes`, nadal wyłącznie według authoritative `snapshot.game.stage`. Zdjęcie wybierane jest standardowym inputem `accept="image/*" capture="environment"`, więc przeglądarka mobilna może zaoferować aparat tylny lub galerię. Nie jest budowany własny subsystem kamery.

Przed uploadem zdjęcie jest odczytywane jako obraz, zmniejszane maksymalnie do 2048 px na dłuższym boku, normalizowane do JPEG i kompresowane od jakości 0.86 do 0.62, aż mieści się w limicie 5 MiB. Nieobsługiwane, zbyt duże (wejściowo ponad 15 MiB), uszkodzone i nieprzetwarzalne pliki dostają kontrolowany komunikat. Preview i Blob są tylko w pamięci karty; SignalR reconnect bez reloadu ich nie usuwa, natomiast refresh przed zaakceptowaniem odpowiedzi wymaga ponownego wyboru zdjęcia.

Photo upload używa istniejącego multipart `POST /api/rooms/{roomCode}/questions/{questionInstanceId}/photo-answers` z `playerId`, `reconnectToken`, `clientSubmissionId` i JPEG `photo`. Drawing exportuje białe płótno PNG 1024×1024 do analogicznego endpointu `drawing-answers` z polem `drawing`. Ten sam `clientSubmissionId`, utrwalony tylko dla danej sesji/pytania/akcji, jest ponownie używany przy retry; backend jest jedynym potwierdzeniem accepted submission. Po refreshu po sukcesie `resume` i private state pokazują stan „wysłano”, bez ponownego formularza.

Canvas używa Pointer Events, z normalizacją współrzędnych względem aktualnego CSS `getBoundingClientRect`, `touch-action: none` i bitmapą 1024×1024 niezależną od rozmiaru CSS; działa dla myszy, dotyku i obsługiwanych pointerów rysika bez utraty jakości na ekranach o wysokim DPR. Ma Undo i potwierdzane Clear, a pusty rysunek jest lokalnie zablokowany. Nie przechowuje bitmapy ani stroke'ów w persistent browser storage; reconnect bez reloadu zachowuje lokalny stan płótna, odświeżenie przed sukcesem może go utracić.

Voting renderuje wyłącznie anonimowe opcje przekazane przez backend (`photoAnswerResults` lub `drawingAnswerResults`), bez autora i bez tokenów w URL. Zdjęcia/rysunki zachowują proporcje przez `object-fit: contain`; podczas ładowania jest stan kontrolowany, a błąd, 404 lub brak URL jest lokalnym placeholderem, który nie wywraca etapu. Text voting pozostaje bez zmian. Results, Round Summary oraz ranking pozostają zakresem kolejnego etapu.

### Feature QA package

Do fizycznego QA 1D utwórz przez Admin osobny, opublikowany pakiet `Web Player 1D QA`: jedna aktywna kategoria i dokładnie cztery aktywne pytania — po jednym `PlayerSelection`, `TextAnswer`, `PhotoAnswer` oraz `DrawingAnswer`. Przy trzech graczach wybierz tylko ten pakiet, wszystkie cztery typy, jedną rundę i cztery pytania. To korzysta z normalnego flow Admin/Published package, nie wymaga ręcznej edycji SQLite i nie zmienia semantyki `RC physical QA`.

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
