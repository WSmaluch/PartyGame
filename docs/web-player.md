# PartyGame Web Player (1F)

Web Player to przeglądarkowy klient tego samego PartyGame API i SignalR Hub, co aplikacja iOS. Po wdrożeniu wejście to `http://HOST:PORT/play/`; konfiguracja runtime jest dostępna pod `/play/config.json`.

Użytkownik wpisuje czteroznakowy kod pokoju oraz nick (2–20 znaków). Formularz korzysta z istniejącego `POST /api/rooms/{roomCode}/players`, a następnie łączy się z `/hubs/game` i wywołuje `AttachPlayer`. Parametr `?room=AB12` uzupełnia kod pokoju; kod jest trimowany i zamieniany na wielkie litery, tak samo jak zwykłe pole formularza.

## QR Join z Display

W Lobby Display pokazuje kod pokoju i QR „Zeskanuj, aby dołączyć”. Kod jest generowany lokalnie przez lekką bibliotekę `qrcode` z runtime `publicAppUrl` Display, dlatego wskazuje właściwy host wdrożenia i URL `http(s)://HOST/play/?room=CODE`, a nie `localhost` ani stały adres LAN. Parametr pokoju jest URL-encoded; QR ani link zapasowy nie zawierają player id, reconnect tokena, danych administratora ani innych sekretów. QR jest widoczny tylko w Lobby — po rozpoczęciu gry Display pokazuje ekran gry, bez zachęcania do niedozwolonego late join.

## Lobby, profil i reconnect

Po join Web Player pokazuje Lobby z kodem pokoju, statusem połączenia, listą graczy, avatarami oraz statusami Ready. Przycisk **Gotowy** wywołuje istniejące SignalR `SetReady`; stan z `RoomSnapshotUpdated` jest zawsze źródłem prawdy.

Zdjęcie profilowe jest wysyłane istniejącym `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo` z `X-Player-Token`. Przeglądarka przyjmuje obraz z galerii lub aparatu (`capture="user"`), normalizuje go po stronie klienta do JPEG maksymalnie 1200 px i nie więcej niż 5 MiB. Wybrany obraz jest tylko krótkotrwałym preview; nie trafia do browser storage.

Storage zawiera wyłącznie `roomCode`, `playerId`, `reconnectToken` i `nickname`. Po odświeżeniu klient najpierw używa istniejącego endpointu `resume`, potem `AttachPlayer`; nie tworzy drugiego gracza. Nieważna lub wygasła sesja jest usuwana, a użytkownik wraca kontrolowanie do Join. SignalR używa automatycznego reconnectu i po odzyskaniu transportu ponownie wykonuje `AttachPlayer`. Druga karta z tą samą sesją nie tworzy gracza: backend zastępuje aktywne connection id starszej karty.

Gdy karta wraca z tła, Web Player ponownie wykonuje bezpieczny `resume` zapisanej sesji, aby odzyskać aktualny authoritative snapshot po uśpieniu mobilnego transportu. Brak sieci jest komunikowany jako kontrolowany stan połączenia; aplikacja nie pokazuje surowych wyjątków ani nie zapisuje mediów w trwałym storage.

## Gameplay: PlayerSelection, tekst i głosowanie

`RoomStarted` oraz każde `RoomSnapshotUpdated` przechodzą przez jeden router oparty wyłącznie na `snapshot.game.stage`. W 1C obsługiwane są `CollectingPlayerSelections`, `CollectingTextAnswers` i `CollectingTextAnswerVotes`; przejście między etapami nigdy nie jest wyliczane lokalnie.

Wybór gracza używa listy zgodnej z klientem iOS (wszyscy pozostali gracze), odpowiedź tekstowa stosuje backendowy limit 150 znaków po trim, a głosowanie korzysta wyłącznie z anonimowych `textResults.votingOptions`. Prywatny event `PlayerPrivateGameStateUpdated` potwierdza odpowiedź tekstową i głos; po refreshu `resume` przywraca ten stan przed pokazaniem formularza.

Każda akcja używa istniejących metod hubu `SubmitPlayerSelectionWithSubmission`, `SubmitTextAnswerWithSubmission` i `SubmitTextAnswerVoteWithSubmission`. Identyfikator `clientSubmissionId` jest stabilny dla tej samej instancji pytania, akcji i karty (`sessionStorage`), więc retry po błędzie sieci nie tworzy drugiej odpowiedzi ani głosu. Timer jest wyłącznie prezentacją deadline serwera; zerowy countdown nie zmienia etapu lokalnie. Sygnalizacja `Ping` koryguje zegar klienta, gdy jest dostępna.

## Gameplay: Photo, Drawing i media voting

Etap 1D obsługuje `CollectingPhotoAnswers`, `CollectingDrawingAnswers`, `CollectingPhotoAnswerVotes` i `CollectingDrawingAnswerVotes`, nadal wyłącznie według authoritative `snapshot.game.stage`. Zdjęcie wybierane jest standardowym inputem `accept="image/*" capture="environment"`, więc przeglądarka mobilna może zaoferować aparat tylny lub galerię. Nie jest budowany własny subsystem kamery.

Przed uploadem zdjęcie jest odczytywane jako obraz, zmniejszane maksymalnie do 2048 px na dłuższym boku, normalizowane do JPEG i kompresowane od jakości 0.86 do 0.62, aż mieści się w limicie 5 MiB. Nieobsługiwane, zbyt duże (wejściowo ponad 15 MiB), uszkodzone i nieprzetwarzalne pliki dostają kontrolowany komunikat. Preview i Blob są tylko w pamięci karty; SignalR reconnect bez reloadu ich nie usuwa, natomiast refresh przed zaakceptowaniem odpowiedzi wymaga ponownego wyboru zdjęcia.

Photo upload używa istniejącego multipart `POST /api/rooms/{roomCode}/questions/{questionInstanceId}/photo-answers` z `playerId`, `reconnectToken`, `clientSubmissionId` i JPEG `photo`. Drawing exportuje białe płótno PNG 1024×1024 do analogicznego endpointu `drawing-answers` z polem `drawing`. Ten sam `clientSubmissionId`, utrwalony tylko dla danej sesji/pytania/akcji, jest ponownie używany przy retry; backend jest jedynym potwierdzeniem accepted submission. Po refreshu po sukcesie `resume` i private state pokazują stan „wysłano”, bez ponownego formularza.

Canvas używa Pointer Events, z normalizacją współrzędnych względem aktualnego CSS `getBoundingClientRect`, `touch-action: none` i bitmapą 1024×1024 niezależną od rozmiaru CSS; działa dla myszy, dotyku i obsługiwanych pointerów rysika bez utraty jakości na ekranach o wysokim DPR. Ma Undo i potwierdzane Clear, a pusty rysunek jest lokalnie zablokowany. Nie przechowuje bitmapy ani stroke'ów w persistent browser storage; reconnect bez reloadu zachowuje lokalny stan płótna, odświeżenie przed sukcesem może go utracić.

Voting renderuje wyłącznie anonimowe opcje przekazane przez backend (`photoAnswerResults` lub `drawingAnswerResults`), bez autora i bez tokenów w URL. Zdjęcia/rysunki zachowują proporcje przez `object-fit: contain`; podczas ładowania jest stan kontrolowany, a błąd, 404 lub brak URL jest lokalnym placeholderem, który nie wywraca etapu. Text voting pozostaje bez zmian.

## Wyniki, podsumowanie rundy i zakończenie

Router obsługuje końcowe snapshoty `ShowingQuestionResults`, `ShowingTextAnswerResults`, `ShowingPhotoAnswerResults`, `ShowingDrawingAnswerResults`, `RoundSummary` i `Completed`. Wyniki są renderowane bezpośrednio z istniejących kontraktów backendu: liczby głosów, znacznika zwycięzcy, autorów ujawnianych przez etap wyników oraz dostępnych danych medium. Uszkodzony, niedostępny lub pusty URL medium pozostawia kontrolowany placeholder zamiast wywracać widok.

Wszystkie normalne etapy backendowego `GameStage` mają widok player-facing: formularze i voting dla etapów collecting, wyniki dla results, ranking dla podsumowań oraz kontrolowane oczekiwanie dla intro, reveal i pauzy Display. Klient nigdy nie przechodzi między etapami lokalnie.

Tabela wyników korzysta wyłącznie z backendowych `ranking`/`rankings` oraz pola `rank`; aplikacja webowa nie oblicza pozycji ani nie przełamuje remisów. Gdy pozycja nie jest dostępna, pokazuje `—`, nigdy domyślne `#1`. To zachowuje ranking konkurencyjny (np. `#1, #2, #2` albo `#1, #1, #3`) zarówno w `RoundSummary`, jak i `Completed`. W `RoundSummary` pokazywany jest też backendowy numer rundy i informacja, czy istnieje kolejna runda; `Completed` pokazuje końcowy ranking.

Każdy snapshot przechodzi przez ochronę `stateVersion`: klient ignoruje opóźniony stan o niższej wersji, więc reconnect ani kolejność callbacków nie cofają użytkownika z wyników lub ekranu końcowego. Po refreshu zapisany gracz wykonuje `resume` i `AttachPlayer`; snapshot zawierający `game` prowadzi z powrotem do gameplay, także gdy serwer oznaczy jego fazę jako `Completed`.

### Feature QA package

Do fizycznego QA utwórz przez Admin osobny, opublikowany pakiet `Web Player Full QA`: jedna aktywna kategoria i dokładnie cztery aktywne pytania — po jednym `PlayerSelection`, `TextAnswer`, `PhotoAnswer` oraz `DrawingAnswer`. Przy trzech graczach wybierz tylko ten pakiet, wszystkie cztery typy, jedną rundę i cztery pytania. Przejdź pełny scenariusz: QR → Join → Lobby → Ready → cztery typy pytań i voting → Results po każdym pytaniu → Round Summary → Completed, następnie odśwież przeglądarkę w wynikach i na ekranie końcowym. To korzysta z normalnego flow Admin/Published package, nie wymaga ręcznej edycji SQLite i nie zmienia semantyki `RC physical QA`.

## Przeglądarki i layout

Wspierane są aktualne Chrome, Edge, Firefox i Safari z Pointer Events, Canvas oraz `URL.createObjectURL`. Atrybut `capture` sugeruje aparat w Chrome/Safari na telefonie, ale przeglądarka może zamiast niego zaoferować galerię. Interfejs jest mobile-first, korzysta z `100dvh`, scrollowalnych list i `safe-area-inset-*`; canvas blokuje scroll tylko na własnym obszarze. Nie dodano service workera, aby runtime config wdrożenia nie był przypadkowo przechowywany w cache.

### Znane ograniczenia walidacji środowiska

Końcowa akceptacja na urządzeniu fizycznym oraz pełny release validator wymagają pełnego Xcode z dostępnym symulatorem iOS. W środowisku z samymi Command Line Tools skrypt `scripts/build-release.sh` może poprawnie zbudować artefakty API i web (`/play/index.html`, `/play/config.json`, assets), ale zatrzymuje się na obowiązkowym `xcodebuild build-for-testing`; manifest, package verify i LAN smoke nie są wtedy deklarowane jako ukończone.

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
