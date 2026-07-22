# Architektura

PartyGame jest projektowany jako serwerowo sterowana gra czasu rzeczywistego. Backend początkowo działa w lokalnej sieci Wi-Fi, ale jego warstwy nie zależą od topologii sieciowej i mogą zostać wdrożone w internecie.

## Zależności

```text
PartyGame.Api
├── PartyGame.GameEngine
│   └── PartyGame.Domain
└── PartyGame.Infrastructure
    ├── PartyGame.Domain
    └── PartyGame.GameEngine
```

- **PartyGame.Api** odpowiada za transport HTTP i SignalR, konfigurację procesu oraz składanie zależności.
- **PartyGame.GameEngine / Infrastructure.Rooms** realizują planowanie, trwałą maszynę stanów, punktację i serwerowe timery dla PlayerSelection, TextAnswer, PhotoAnswer i backendowego DrawingAnswer.
- **PartyGame.Domain** zawiera `GameRoom`, `Player`, `PlayerSession`, `RoomSettings`, fazy i walidację niezależną od technologii. Nie zależy od ASP.NET Core, SignalR ani EF Core.
- **PartyGame.Infrastructure** odpowiada za SQLite/EF Core, generowanie kodów i tokenów, usługę pokojów, blokady per pokój oraz `IMediaStorage` z lokalną implementacją normalizującą obrazy.

Klienci iOS, ekran TV i panel React nigdy nie są źródłem prawdy. Wysyłają intencje, a stan autorytatywny i obliczenia pozostają na serwerze.

## Wersjonowanie pakietów treści

`GamePackage.Id` jest ID wersji, nie ID rodziny. `LogicalPackageId` łączy historię, a `Version` jest rosnącym numerem. Draft można edytować; Published jest niezmiennym snapshotem do nowych pokoi; Archived nie przyjmuje nowych pokoi, lecz pozostaje dostępny dla już przypiętych gier. Utworzenie Draftu z Published/Archived wykonuje głęboką kopię kategorii i pytań z nowymi ID i tokenami. Jeden Draft na rodzinę jest egzekwowany lokalną blokadą oraz częściowym unikalnym indeksem SQLite, więc wyścig `create-draft` zwraca 409 zamiast tworzyć dwie wersje.

Publikowanie, archiwizowanie oraz mutacje Draftu są chronione tokenami współbieżności. Konflikty `DbUpdateConcurrencyException`, wyścigi statusu i unikalny indeks są tłumaczone na jawne błędy domenowe, bez HTTP 500. Tworzenie pokoju z jawnym `contentPackageVersionId` bierze blokadę tej wersji i weryfikuje Published tuż przed zapisem pokoju; archiwizacja używa tej samej blokady. Stary request bez ID nadal wybiera domyślny Published package. `GameRoom.ContentPackageVersionId` jest trwałym FK: restart, publikacja v2 i archiwizacja v1 nie zmieniają historycznego przypisania ani treści planowanej dla istniejącego pokoju.

iOS PhotoAnswer przygotowuje lokalny JPEG i przechowuje tymczasowy draft z idempotentnym `clientSubmissionId`, ale przechodzi do stanu zapisanego dopiero po odpowiedzi backendu lub prywatnym evencie. Display korzysta wyłącznie z publicznych anonimowych opcji aż do etapu wyników. Oba klienty odrzucają starsze snapshoty; prywatny stan iOS jest dodatkowo związany z graczem i `questionInstanceId`. Obrazy gry są cache’owane tylko w pamięci i czyszczone po zmianie pytania.

## Komunikacja

REST obsługuje tworzenie/dołączenie, publiczne migawki, sprawdzanie sesji i transfer zdjęć. SignalR synchronizuje pełne migawki lobby. `RoomService` jest wspólnym punktem reguł dla obu transportów, a warstwa API tylko mapuje DTO i rozgłasza zdarzenia.

ConnectionId pozostaje w singletonowym rejestrze in-memory i nigdy nie trafia do encji. Rejestr pilnuje jednego aktywnego połączenia na gracza i jednego Display na pokój oraz rozpoznaje spóźnione rozłączenia. Zmiany pokoju są serializowane przez `SemaphoreSlim` per kod; dzięki temu sprawdzenie warunku i zapis `Lobby → Started` następują dokładnie raz w pojedynczym procesie. SQLite ma unikalne indeksy kodu i `(RoomId, NormalizedNickname)`.

Każda publiczna zmiana zwiększa `StateVersion`. Pełna migawka jest jedynym publicznym formatem synchronizacji; nie zawiera sesji, hashy, ścieżek ani połączeń. Lokalny HTTP jest rozwiązaniem deweloperskim; wdrożenie internetowe wymaga HTTPS i docelowych zasad CORS.

Media PhotoAnswer są zatwierdzane dwuetapowo: storage zapisuje znormalizowane warianty, a baza wiąże je z `MediaAsset` i submission w transakcji. Błąd commit uruchamia kompensacyjne usunięcie obu wariantów. Restart zachowuje SQLite i katalog mediów; brak fizycznego pliku jest stanem kontrolowanym i zwraca 404 bez destabilizacji pokoju. Wyścigi upload/vote/timeout/display są serializowane istniejącą blokadą pokoju, a indeksy unikalne stanowią drugą linię ochrony.

DrawingAnswer rozszerza tę samą granicę `IMediaStorage` o PNG. Detektor tuszu działa przed finalnym zapisem, oba warianty przechodzą przez `.tmp`, a publiczne kontrakty pozostają anonimowe aż do wyników. iOS utrzymuje lokalny, izolowany per pokój/gracz/pytanie draft stroke’ów, renderuje biały PNG 1024×1024 i wysyła go przez istniejący idempotentny endpoint. Display nie dekoduje prywatnego stanu: renderuje wyłącznie liczniki w collecting, anonimowe obrazy w reveal/voting oraz autorów dopiero w results. Oba klienty odrzucają starsze `stateVersion` i bezpiecznie obsługują 404 mediów.

## Wprowadzone zmiany techniczne i lekcje z Etapu 5B

- **Zarządzanie cyklem życia asynchronicznych zadań**: W środowisku testowym iOS (`GameSessionStoreTests`) zaobserwowano niestabilność (flakiness) wywoływaną przez przeżywające cykl `tearDown` asynchroniczne odpytywania stanu (`privateStateRefreshTask`). Architektura wymaga ścisłego anulowania (`cancel()`) aktywnych tasków i oczekiwania na ich zamknięcie na etapie czyszczenia pamięci, co całkowicie likwiduje wyścigi do dawno usuniętych mocków.
- **Optymalizacja deskryptora SignalR w Swift**: Proces dekodowania obiektów o dynamicznych strukturach (np. słowniki w `LocalizedText`) ze strony Swift SignalR generował zbędne "round-trips" do postaci skalarnej JSON. Architektura dekoderów preferuje teraz natychmiastową próbę odczytania złożonego słownika (jak w docelowym API) zamiast rzutowania z powrotem.
- **Zunifikowany kontrakt postępu**: Standaryzacja przesyłania postępu wszystkich typów gier (w tym DrawingAnswer) na jednolity standard postępu z polami `submittedPlayers` oraz `requiredPlayers`. Klient iOS jak i React od teraz polegają na tych samych stałych, bez podziału na dedykowane, specyficzne dla typu pytania właściwości.

## Deferred E2E hardening

Status: **Implemented partially — execution deferred**.

`MixedGameClientE2ETests` posiada prawdziwe interakcje iOS obejmujące Join, systemowy `PhotosPicker`, zapis profilu, Ready oraz pierwszą odpowiedź DrawingAnswer. Pełne uruchomienie mixed-client wymaga jeszcze deterministycznego setupu Published package, pokoju oraz skryptowanych klientów SignalR w orkiestratorze.

Jest to dług infrastruktury E2E, a nie brak funkcjonalności produkcyjnej Etapu 6A. Zakres został przeniesiony do późniejszego hardeningu i nie blokuje odbioru Admin Content Editora.
