# Etap 7 — Mixed Client E2E Hardening

Status całego Etapu 7: w toku.

| Podetap                                     | Status        |
| ------------------------------------------- | ------------- |
| 7.1 — deterministyczna orkiestracja         | ukończony     |
| 7.2 — pełny przebieg czterech typów pytań   | nierozpoczęty |
| 7.3 — reconnect i `stateVersion`            | nierozpoczęty |
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

## Granica 7.2

7.1 kończy się po potwierdzeniu pojedynczego automatycznego startu. Nie przesyła odpowiedzi,
nie prowadzi głosowań ani nie dochodzi do `Completed`. Pełna rozgrywka PlayerSelection,
TextAnswer, PhotoAnswer i DrawingAnswer pozostaje zakresem 7.2.
