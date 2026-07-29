# Etap 7 — Mixed Client E2E Hardening

Status całego Etapu 7: w toku.

| Podetap                                     | Status        |
| ------------------------------------------- | ------------- |
| 7.1 — deterministyczna orkiestracja         | ukończony     |
| 7.2 — pełny przebieg czterech typów pytań   | ukończony |
| 7.3 — reconnect i `stateVersion`            | w toku |
| 7.3A.1 — rzeczywiste obserwacje iOS          | ukończony |
| 7.3A.2 — obserwacje scripted players/backend | ukończony |
| 7.3A.3 — agregator ledgeru pięciu klientów    | ukończony |
| 7.4 — stabilizacja wielokrotnych przebiegów | nierozpoczęty |

## Cel 7.1

`scripts/test-mixed-client-e2e.sh` uruchamia izolowany backend SQLite, a potem tworzy
deterministyczny Published package i pokój przed uruchomieniem prawdziwych klientów.
Test potwierdza jeden automatyczny start produkcyjnej gry z iOS, Display oraz dwoma
scripted players.

## Kolejność procesów

1. Skrypt tworzy jeden katalog tymczasowy przez `mktemp`: SQLite, media root, migawkę
   `apps/ios` z `git archive`, DerivedData, SourcePackages, cache pakietów, `xcresult`
   i logi Xcode. Migawka omija blokadę `NSFileCoordinator` Xcode dla projektu otwartego
   bezpośrednio z katalogu roboczego; bieżące zmiany iOS są na nią nakładane.
2. `PartyGame.MixedE2EOrchestrator` tworzy przez Admin REST draft package z jednym
   pytaniem każdego typu, publikuje go i tworzy pokój przez produkcyjny REST z jawnym
   `contentPackageVersionId`.
3. Orkiestrator tworzy dwóch scripted players, przesyła ich zdjęcia profilowe przez
   produkcyjny endpoint i dołącza ich do produkcyjnego huba SignalR. Nie ustawia ich
   jeszcze jako Ready.
4. Po zapisaniu publicznego stanu koordynacji skrypt uruchamia Vite, Display Playwright
   oraz XCUITest. iOS przechodzi normalnie przez Join, PhotosPicker, zapis profilu i Ready.
5. Display wpisuje kod w produkcyjnym ekranie, wykonuje realny `AttachDisplay` i czeka,
   aż lobby pokaże trzech graczy.
6. Dopiero po markerach `display-attached` i `ios-ready` orkiestrator ustawia Ready dla
   własnych graczy, obserwuje `RoomStarted` na ich realnym połączeniu SignalR i waliduje
   stan pokoju.

## Stan koordynacji

Jedynym publicznym źródłem prawdy między procesami jest
`$PARTYGAME_E2E_COORDINATION_DIR/coordination.json`. Powstaje atomowo dopiero po utworzeniu
pokoju i zawiera: `backendUrl`, `roomCode`, `contentPackageVersionId`, nazwę iOS,
oczekiwany Display i nazwy scripted players. Nie zawiera reconnect tokenów, zdjęć ani
sekretów. Id pokoju nie jest wystawiony przez istniejący publiczny kontrakt REST;
orkiestracja celowo używa publicznego `roomCode` bez zmiany kontraktu tylko dla testu.

Markery w tym samym katalogu opisują postęp (`display-attached`, `ios-ready`,
`game-started`). `outcome.json` zapisuje wyłącznie bezpieczne podsumowanie: etap, fazę,
`stateVersion`, jawny ID package i liczbę zdarzeń `RoomStarted`.

## Brak backdoorów produkcyjnych

Etap 7.1 nie dodaje endpointu startującego grę, auto-joinu, auto-ready, wstrzykiwania
tokenów ani obejścia PhotosPicker. iOS otrzymuje wyłącznie konfigurację testową i wykonuje
zwykłą ścieżkę użytkownika. Scripted players używają REST, uploadu zdjęć profilowych i
SignalR tak jak zwykli klienci.

## Cleanup i diagnostyka

Skrypt rejestruje PID-y API, Vite, orkiestratora, xcodebuild i Playwright. Trap dla PASS,
FAIL, SIGINT oraz SIGTERM zamyka wyłącznie te PID-y, usuwa katalog tymczasowy (SQLite,
media, coordination state, `xcresult`, DerivedData i artefakty Playwright). Nie używa
`killall`.

Przy błędzie skrypt zachowuje poza repozytorium krótki katalog diagnostyczny z etapem,
kodem wyjścia, bezpiecznym `outcome.json`, publicznym stanem koordynacji i ostatnimi
fragmentami logów procesów. Nie kopiuje tokenów, obrazów ani danych profili.

## Xcode i fazy

Wymagany jest Xcode wskazany przez `DEVELOPER_DIR` oraz dostępny simulator
`86B8118B-E2A6-4947-A716-84F6FA0850D9` (domyślnie iPhone 17 Pro). Skrypt wykonuje osobno
preflight, kontrolowany restart tego simulatora, `xcodebuild -resolvePackageDependencies`,
`build-for-testing` oraz `test-without-building` wyłącznie dla `MixedGameClientE2ETests`.

Każda faza Xcode ma własny PID, timeout, log i status z kodem wyjścia. Po resolve build
używa `-disableAutomaticPackageResolution`; test korzysta z wygenerowanego `.xctestrun`,
więc nie rozwiązuje pakietów ponownie. Timeout pozostawia diagnostykę w
`$TMPDIR/partygame-mixed-e2e-failure.*`. Brak postępu przed `Resolve Package Graph`
oznacza błąd otwarcia projektu/Xcode, a błąd markera lub `outcome.json` — błąd orkiestracji.

## Uruchomienie 7.1

```bash
./scripts/test-mixed-client-e2e.sh
```

Opcjonalnie `IOS_DESTINATION_ID` wskazuje konkretny simulator. Skrypt wymaga lokalnych
zależności .NET, Display Playwright i Xcode oraz dostępnego simulatora `iPhone 17 Pro`,
jeśli zmienna nie jest podana.

`PARTYGAME_E2E_RUN_MODE=ios-only` uruchamia przygotowany backend i pokój oraz tylko
XCUITest do profilu i Ready; tryb domyślny pozostaje pełnym przebiegiem 7.1.

## Etap 7.2 — pełny przebieg czterech typów

7.2 rozszerza ten sam izolowany przebieg do `Completed`. Fixture Published package zawiera
dokładnie po jednym pytaniu `PlayerSelection`, `TextAnswer`, `PhotoAnswer` i `DrawingAnswer`.
Nie wymusza ich kolejności: produkcyjny `GamePlanner` zachowuje własne tasowanie wzorca pytań.

Orkiestrator zapisuje typ każdego pytania z odpowiedzi Admin REST podczas tworzenia fixture,
a następnie odczytuje ze snapshotu aktywne `questionId`, fazę oraz `stateVersion`. Typ aktywnego
pytania ustala przez mapę opublikowanego pakietu i dodatkowo sprawdza jego zgodność z produkcyjną
fazą. Odrzuca obce lub ponownie użyte ID pytania i uruchamia odpowiednią akcję dopiero po
obserwacji Display. iOS korzysta z normalnych ekranów wyboru gracza, odpowiedzi tekstowej,
PhotosPicker, gestu rysowania i głosowania. Dwaj scripted players używają wyłącznie istniejących
REST uploadów oraz metod produkcyjnego SignalR. Do `coordination` trafiają jedynie bezpieczne
markery etapów i aktywny publiczny stan pytania, nigdy tokeny ani prywatne ID odpowiedzi.
Podstawowe markery iOS to `ios-player-selection-submitted`, `ios-text-submitted`,
`ios-photo-submitted`, `ios-drawing-submitted` i `ios-completed-observed`.

iOS wybiera obraz przez normalny PhotosPicker i przesyła JPEG przygotowany przez produkcyjny
pipeline klienta. Scripted players tworzą rozróżnialne, poprawne JPEG dla PhotoAnswer oraz PNG
dla DrawingAnswer i wysyłają je istniejącymi endpointami multipart. Rysunek iOS powstaje przez
rzeczywisty gest XCUITest na produkcyjnym canvasie.

Deterministyczność 7.2 oznacza stały skład pakietu, kompletność przebiegu i mierzalne asercje,
a nie stałą kolejność pytań. Każda z 24 permutacji czterech typów jest prawidłowa.

Po zakończeniu orkiestrator wymaga: czterech unikalnych `questionId`, dokładnie jednego pytania
każdego typu, braku piątego pytania, monotonicznego `stateVersion`, pojedynczego `RoomStarted`,
`RoomPhase = Completed` oraz rankingu z wszystkimi trzema graczami. Display obserwuje realne
ekrany zbierania, ujawniania, głosowania i wyników właściwe dla aktualnie wylosowanego typu oraz
końcowy ranking.

## Etap 7.3 — reconnect i monotoniczny `stateVersion`

W tym samym pełnym przebiegu iOS wykonuje dokładnie jedno kontrolowane zakończenie
aplikacji po pierwszej zaakceptowanej akcji. Po zwykłym ponownym uruchomieniu używa
istniejącego zapisu Keychain oraz produkcyjnego `resume` i `AttachPlayer`; test nie
wstrzykuje tokenu ani nie tworzy nowego gracza. Display później przeładowuje produkcyjną
stronę i odzyskuje pokój przez zapisany kod oraz normalny `AttachDisplay`.

Koordynacja zapisuje bezpieczne markery reconnectu i wersje przed/po w osobnych plikach.
Końcowy ledger obejmuje backend/orkiestrator, iOS, Display i obu scripted players. Dla
każdego klienta zaakceptowany `stateVersion` nie może maleć; wersja po recovery musi być
co najmniej wersją sprzed rozłączenia. `outcome.json` potwierdza jeden reconnect każdego
klienta, tego samego gracza iOS, brak duplikatów odpowiedzi/głosów, cztery typy pytań,
`Completed` i ranking trzech graczy. Diagnostyka awarii zachowuje wyłącznie bezpieczny
ledger, fazę i wersje — bez tokenów ani danych mediów.

Etap 7.4 pozostaje zakresem stabilizacji wielokrotnych przebiegów (w tym seria 5/5).

## Etap 7.3A.1 — rzeczywiste obserwacje iOS

`LobbyView` i `GameRouterView` wystawiają niewidoczny element dostępności wyłącznie z
aktualnie zaakceptowanego `RoomSnapshot`. Oba widoki korzystają ze wspólnego formattera
`SnapshotAccessibilityMetadata`; element nie zmienia wyglądu ani routingu i nie zawiera
tokenu, kodu sekretnego ani danych prywatnych. Identifier ma dokładnie format:

```text
game.snapshot|stateVersion=<liczba>|phase=<faza>|questionId=<UUID-lub-pusty>
```

`MixedGameClientE2ETests` wymaga dokładnie jednego takiego elementu i zapisuje atomowo
obserwacje po sześciu rzeczywistych punktach przebiegu: `snapshot-lobby-accepted`,
`snapshot-game-started`, `snapshot-before-disconnect`, `snapshot-after-recovery`,
`snapshot-after-post-reconnect-action` oraz `snapshot-completed`. Recovery wymaga wersji
co najmniej takiej jak przed rozłączeniem; obserwacja po reconnect następuje dopiero po
potwierdzonej akcji iOS oraz ponownym wyrenderowaniu stanu.

Parser identifiera odrzuca niepełny, zduplikowany i niejednoznaczny format. Wspólny tracker
akceptuje niemalejące `stateVersion` (w tym duplikaty), zlicza zaakceptowane obserwacje i
odrzuca regresję bez zmiany ostatniej zaakceptowanej wersji. Writer sprawdza katalog
koordynacji, numeruje pliki `ios-observation-000001.json`, zapisuje przez plik tymczasowy
i rename, nie nadpisuje kolizji oraz po zapisie dekoduje JSON ponownie.

Testy obejmują formatter dla Lobby, aktywnej gry i Completed oraz jego deterministyczność;
parser poprawnego i błędnych formatów; tracker dla sekwencji `10, 11, 11, 12` i regresji
`10, 12, 11`; a także writer dla numeracji, dekodowalności, braku artefaktu tymczasowego,
kolizji i brakującego katalogu. Kontrolowany `resolvePackageDependencies` zakończył się
kodem `0` dla `SignalRClient 1.0.0`, a kontrolowany `build-for-testing` tym samym
DerivedData i SourcePackages — `TEST BUILD SUCCEEDED`. Formatter (4), parser (2), tracker
(2) i writer (3) przeszły jako celowane `test-without-building`. Pełny Mixed Client E2E oraz
seria 5/5 pozostają poza 7.3A.1.

Status: 7.3A.1 — ukończony; 7.3A — nadal nieukończony; 7.3 — nadal nieukończony.

## Etap 7.3A.2 — obserwacje scripted players i backendu

Orkiestrator zapisuje wspólny, bezpieczny model obserwacji z polami `client`, `event`,
`stateVersion`, `phase`, `questionId` i czasem UTC. Nie zawiera on tokenów, sekretów,
mediów ani danych profili. Każdy zapis jest osobnym plikiem JSON w katalogu koordynacji,
ma własną sekwencję per klient i jest wykonywany przez plik tymczasowy, atomowy rename,
sprawdzenie istnienia oraz ponowne dekodowanie.

Player A i Player B mają całkowicie niezależne rekordery: osobny tracker, ostatnią
zaakceptowaną wersję, liczniki obserwacji/regresji, sekwencję i pliki odpowiednio
`scripted-player-a-observation-*.json` oraz `scripted-player-b-observation-*.json`.
Snapshot jest rejestrowany dopiero po akceptacji odpowiedzi `AttachPlayer` albo zdarzeń
SignalR `RoomSnapshotUpdated` i `RoomStarted`. Następna akcja scripted players wymaga,
aby obaj posiadali zgodny zaakceptowany snapshot aktywnego pytania. Wersja starsza jest
odrzucana, zwiększa licznik regresji i kończy scenariusz błędem; duplikat tej samej
wersji nie jest regresją ani nową obserwacją.

Backendowy recorder obserwuje snapshot utworzenia pokoju oraz odpowiedzi `GET /api/rooms`,
z których orkiestrator faktycznie podejmuje decyzje. Obejmuje to Lobby, start gry, kolejne
pytania i fazy oraz Completed. `state-version-ledger.json` i końcowy `stateVersion` korzystają
z ostatniej zaakceptowanej obserwacji backendu, nie z wartości syntetycznej.

Celowane testy C# obejmują monotoniczność i regresję trackera, niezależność A/B, numerację,
dekodowanie i kolizje writera, walidację modelu oraz ścieżkę backendu Lobby → Started →
Completed z regresją. Wynik: 10/10 PASS. `dotnet build` orkiestratora zakończył się PASS
(ostrzeżenie NU1900 o niedostępnej usłudze podatności nie wpływa na wynik). Izolowany build
Displaya w czystej kopii poza repozytorium przeszedł: `npm ci` i `npm run build` exit 0.

Status: 7.3A.1 — ukończony; 7.3A.2 — ukończony; 7.3A — nadal nieukończony; 7.3 — nadal nieukończony.

## Etap 7.3A.3 — agregator ledgeru pięciu klientów

`StateVersionLedgerAggregator` odczytuje wyłącznie rzeczywiste pliki obserwacji pięciu
klientów: `ios`, `display`, `scripted-player-a`, `scripted-player-b` i `backend`. Wzorzec
każdego producenta to `<client>-observation-<numer>.json`; pliki tymczasowe są pomijane,
numer jest parsowany liczbowo, a sekwencja musi zaczynać się od 1 i nie może zawierać luk
ani kolizji. Dzięki temu kolejność nie zależy od listowania systemu plików ani timestampu.

Każdy JSON jest odczytywany dokładnie raz i rygorystycznie walidowany: dozwolone są dokładnie
`client`, `event`, `stateVersion`, `phase`, `questionId` i `timestampUtc`. Brak pola,
nieznane pole, ujemna wersja, klient niezgodny z nazwą pliku lub czas niebędący UTC oznaczają
FAIL. Agregator zachowuje rzeczywiste obserwacje i per klient oblicza ich liczbę, minimum,
maximum, pierwszą/ostatnią wersję i czas, eventy, regresje oraz diagnostykę błędów.

Monotoniczność jest oceniana w kolejności numerów plików; wersje równe są dozwolone,
a malejące powodują `state-version-regression`. Cofnięcie `timestampUtc` także jest FAIL.
iOS musi mieć dokładnie po jednym `snapshot-before-disconnect` i
`snapshot-after-recovery`, a Display `snapshot-before-reload` i
`snapshot-after-reconnect`; odzyskana wersja nie może być starsza. Ostatnia rzeczywista
obserwacja backendu jest `finalBackendStateVersion`, a żadna końcowa wersja klienta nie może
być od niej większa.

Wynik jest zapisywany atomowo jako `state-version-ledger.json` (schemaVersion 1, status,
deterministyczna lista failures, finalna wersja backendu oraz ledger każdego klienta).
`outcome.json` dostaje z niego faktyczne liczniki obserwacji i regresji, wersje reconnectu,
`finalBackendStateVersion` oraz status i liczbę błędów ledgeru; nie używa stałych zastępczych.

Testy agregatora obejmują pełny PASS pięciu klientów, brak każdego klienta, regresje wszystkich
producentów, reconnecty, wersje wyprzedzające backend, niepoprawny JSON i pola, kolizje/luki,
sortowanie po sekwencji, cofnięcie timestampu i atomowy zapis ledgeru. Pełny projekt testowy
orkiestratora: 37 PASS. Status: 7.3A.1, 7.3A.2 i 7.3A.3 — ukończone; 7.3A oraz 7.3 — nadal
nieukończone. Następny zakres: 7.3A.4 — trwałe dowody PASS/FAIL, kody procesów i jeden pełny
przebieg integracyjny.

## Etap 7.3A.4 — trwałe dowody i pełny PASS

Kontrakt snapshotu rozdziela ID definicji pytania (`question.id`) od ID uruchomionej instancji
(`question.instanceId`). API propaguje oba pola, a iOS zachowuje kompatybilny fallback tylko dla
starszych serwerów. Wszystkie operacje zależne od aktywnego pytania medialnego — upload zdjęcia,
upload rysunku, głosowanie na zdjęcie i rysunek, private state oraz markery orkiestratora — używają
tego samego `question.instanceId`; nie ma translacji opartej na kolejności fixture ani na ID
szablonu.

Przy wejściu w `CollectingDrawingAnswers` iOS odświeża private state, dzięki czemu ekran rysowania
nie opiera się na przestarzałej odpowiedzi po reconnect. Dostępność SwiftUI pozostawia identyfikator
rootu widoku na właściwym elemencie; XCUITest obserwuje końcowy `Completed` jako `StaticText`.
Orkiestrator traktuje zakończenie gry zgodnie z publicznym `game.stage = Completed` (techniczna
faza pokoju pozostaje `Started`) i zapisuje w outcome semantyczne `RoomPhase = Completed`.

Skrypt E2E rejestruje PID i status backendu, Vite, Playwright, orkiestratora oraz Xcode, nie myli
poprawnie ukończonego klienta pomocniczego z awarią i zawsze zachowuje bezpieczny bundle dowodowy
poza repozytorium. Bundle zawiera `outcome.json`, `state-version-ledger.json`,
`process-exit-codes.json`, `run-summary.txt`, logi pięciu procesów oraz obserwacje pięciu klientów.

Pełny przebieg PASS z 2026-07-29 jest zachowany w
`/private/tmp/partygame-mixed-e2e-pass.tyMGUy`. Outcome potwierdza `RoomStartedCount = 1`,
cztery pytania (po jednym `PlayerSelection`, `TextAnswer`, `PhotoAnswer`, `DrawingAnswer`),
`RoomPhase = Completed`, ranking 3, finalne `stateVersion = 52`, brak duplikatów odpowiedzi i
głosów. Ledger przeszedł bez błędów: iOS 6 obserwacji (17 → 21 po reconnect), Display 4 (22 → 24),
scripted player A 44, scripted player B 43, backend 29; regresje każdego klienta wynoszą 0.
Playwright, orkiestrator, Xcodebuild i główny skrypt zakończyły się kodem 0, a cleanup również 0.

Status: 7.3A.1 — ukończony; 7.3A.2 — ukończony; 7.3A.3 — ukończony; 7.3A.4 — ukończony;
7.3A — ukończony; 7.3B — niewykonany; 7.3 — nadal nieukończony.
