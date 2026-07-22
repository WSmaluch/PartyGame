# PartyGame

PartyGame to serwerowo sterowana gra imprezowa z klientem iOS, obowiązkowym ekranem TV i panelem administratora. Backend obsługuje równoległe pokoje, reconnect, pauzę Display, trwałe timery i cztery typy pytań: `PlayerSelection`, `TextAnswer`, `PhotoAnswer` oraz `DrawingAnswer`. Etap 5B dodaje natywny canvas iOS, upload PNG, głosowanie oraz publiczne ekrany Display dla DrawingAnswer.

## Technologie

- ASP.NET Core 10 i C# — API oraz przyszły silnik gry;
- SignalR — komunikacja czasu rzeczywistego;
- Entity Framework Core 10 i SQLite — początkowa trwałość danych;
- Serilog — logowanie strukturalne;
- Swagger/OpenAPI — dokumentacja HTTP w środowisku deweloperskim;
- xUnit i `WebApplicationFactory` — testy;
- SwiftUI i Observation dla iOS 17+;
- React 19, TypeScript, Vite, React Router i oficjalny klient SignalR dla aplikacji webowych;
- Vitest, React Testing Library, ESLint i Prettier dla jakości klientów React.

Wybrano .NET 10, ponieważ jest aktualną stabilną wersją zainstalowaną w środowisku. SDK .NET 8 nie było dostępne. Wersję SDK przypina `global.json`.

## Struktura repozytorium

```text
apps/
├── ios/                    # aplikacja SwiftUI i testy XCTest
├── display-web/            # ekran TV, port 5173
└── admin-web/              # panel administracyjny, port 5174
server/
├── PartyGame.Api/          # HTTP, SignalR, DI i start procesu
├── PartyGame.Domain/       # warstwa domenowa bez zależności technologicznych
├── PartyGame.GameEngine/   # przyszłe reguły gry i abstrakcja zegara
├── PartyGame.Infrastructure/ # EF Core, SQLite i migracje
└── PartyGame.Tests/        # testy integracyjne i jednostkowe
contracts/
├── api/
├── signalr/
└── examples/
content/default-packages/   # przyszłe domyślne pakiety treści
docs/                       # architektura i workflow
PartyGame.sln
```

## Wymagania

- macOS;
- .NET SDK 10.0.201 lub nowszy zgodny patch wersji 10.0;
- Node.js 20+ i npm 10+;
- Xcode z runtime'em iOS 17 lub nowszym;
- telefon i Mac w tej samej sieci Wi-Fi do testów LAN;
- wolny port TCP `5050`.

Sprawdź środowisko poleceniem `dotnet --info`.

## Przywracanie i budowanie

Z katalogu głównego repozytorium:

```bash
dotnet tool restore
dotnet restore
dotnet build
dotnet test
```

## Migracje SQLite

Repozytorium zawiera pierwszą migrację technicznej tabeli `DatabaseMetadata`. Dodanie kolejnej migracji:

```bash
dotnet ef migrations add NazwaMigracji \
  --project server/PartyGame.Infrastructure \
  --startup-project server/PartyGame.Infrastructure \
  --output-dir Persistence/Migrations
```

Ręczne zastosowanie migracji:

```bash
dotnet ef database update \
  --project server/PartyGame.Infrastructure \
  --startup-project server/PartyGame.Infrastructure
```

W środowisku `Development` API automatycznie stosuje oczekujące migracje przy starcie. W innych środowiskach migracje muszą być wykonane osobno.

## Uruchomienie backendu

```bash
dotnet run --project server/PartyGame.Api
```

Profil developerski nasłuchuje na wszystkich interfejsach pod portem `5050` — nie zawiera na stałe adresu konkretnego Maca.

- `http://localhost:5050` działa tylko na Macu, na którym uruchomiono serwer.
- `http://ADRES_IP_MACA:5050` jest adresem używanym przez inne urządzenie w tej samej sieci.

Kontrola zdrowia i Swagger:

```bash
curl http://localhost:5050/health
open http://localhost:5050/swagger
```

Hub SignalR jest dostępny pod `/hubs/game`; metoda diagnostyczna `Ping` zwraca `status: pong` i czas UTC serwera. REST lobby zaczyna się od `POST /api/rooms`. Pełny scenariusz i przykłady `curl` opisuje [Etap 1A i 1B](docs/stage-01-client-lobby.md).

Zdjęcia profilowe trafiają domyślnie do systemowego katalogu tymczasowego `${TMPDIR}/PartyGame/media`, a nie do drzewa źródeł. Docelowy katalog można ustawić przez `MediaStorage:RootPath` w konfiguracji. Testy używają osobnych katalogów tymczasowych i usuwają je razem z bazą.

Odpowiedzi zdjęciowe używają `MediaStorage:RootPath` i JPEG. Rysunki DrawingAnswer korzystają z tego samego storage, zachowują PNG i odrzucają pusty canvas. Media są udostępniane wyłącznie przez `/api/media/{mediaAssetId}/{variant}`. Dema regresyjne: `./scripts/demo-player-selection-game.sh`, `./scripts/demo-text-answer-game.sh`, `./scripts/demo-photo-answer-game.sh`, `./scripts/demo-drawing-answer-game.sh` oraz dema mieszane.

## Test z iPhone'a w tej samej sieci Wi-Fi

1. Połącz Maca i iPhone'a z tą samą siecią Wi-Fi. Sieć nie może izolować klientów.
2. Znajdź adres Wi-Fi Maca:

   ```bash
   ipconfig getifaddr en0
   ```

   Jeśli wynik jest pusty, sprawdź interfejs poleceniem `networksetup -listallhardwareports`, a następnie użyj właściwego identyfikatora w `ipconfig getifaddr`, np. `en1`.
3. Uruchom API i pozostaw terminal otwarty.
4. W Safari na iPhonie otwórz `http://ADRES_IP_MACA:5050/health`, np. `http://192.168.1.25:5050/health`.

Oczekiwana odpowiedź ma `status` równy `ok` i `service` równy `PartyGame.Api`.

## Typowe problemy na macOS

- Gdy pojawi się pytanie zapory, zezwól `dotnet` na połączenia przychodzące. Ustawienie można sprawdzić w **Ustawienia systemowe → Sieć → Zapora sieciowa → Opcje**.
- Firmowa lub gościnna sieć Wi-Fi może stosować izolację klientów. Wtedy urządzenia nie zobaczą się mimo poprawnych adresów.
- VPN może zmienić routing; na czas diagnostyki odłącz VPN.
- Sprawdź, czy serwer nasłuchuje na `0.0.0.0:5050`, a nie tylko `localhost`, oraz czy port nie jest zajęty.
- iOS może wymagać zgody aplikacji na dostęp do sieci lokalnej. Safari zwykle pozwala od razu sprawdzić endpoint.

HTTP służy wyłącznie do lokalnego developmentu. Wdrożenie poza zaufaną siecią lokalną musi korzystać z HTTPS oraz docelowych zasad bezpieczeństwa i CORS.

## Uruchamianie klientów

Display:

```bash
cd apps/display-web
npm install
npm run dev
```

Otwórz `http://localhost:5173/display`.

Admin:

```bash
cd apps/admin-web
npm install
npm run dev
```

Otwórz `http://localhost:5174/admin`.

Obie aplikacje odczytują adres serwera z `VITE_API_BASE_URL`. Skopiuj `.env.example` lub edytuj `.env.development`. Zmienna domyślnie wskazuje `http://localhost:5050`.

Aplikację iOS uruchom z [projektu Xcode](apps/ios/PartyGame.xcodeproj). W symulatorze `localhost` oznacza Maca i działa z lokalnym backendem. Na fizycznym iPhonie `localhost` oznacza telefon, dlatego w ekranie ustawień trzeba wpisać `http://ADRES_IP_MACA:5050`.

## Kolejność uruchamiania systemu

1. Uruchom backend: `dotnet run --project server/PartyGame.Api`.
2. Uruchom Display na porcie `5173`.
3. Uruchom Admin na porcie `5174`.
4. Uruchom aplikację `PartyGame` w symulatorze lub na iPhonie.
5. Sprawdź, czy wszystkie klienty pokazują backend jako online; web powinien dodatkowo pokazać `SignalR: Połączony` oraz `pong`.

## Dokumentacja

- [Architektura](docs/architecture.md)
- [Workflow deweloperski](docs/development-workflow.md)
- [Architektura klientów](docs/client-architecture.md)
- [Etap 1A i 1B — lobby](docs/stage-01-client-lobby.md)
- [Etap 4A — backend PhotoAnswer](docs/stage-04a-photo-answer-engine.md)
- [Etap 4B — klienci PhotoAnswer](docs/stage-04b-photo-answer-clients.md)
- [Etap 5A — backend DrawingAnswer](docs/stage-05a-drawing-answer-engine.md)
- [Etap 5B — klienci DrawingAnswer](docs/stage-05b-drawing-answer-clients.md)
- [Założenia REST](contracts/api/README.md)
- [Kontrakt REST pokojów](contracts/api/rooms.md)
- [Założenia SignalR](contracts/signalr/README.md)
- [Kontrakt SignalR lobby](contracts/signalr/lobby.md)

## Deferred E2E hardening

Status: **Implemented partially — execution deferred**.

`MixedGameClientE2ETests` posiada prawdziwe interakcje iOS obejmujące Join, systemowy `PhotosPicker`, zapis profilu, Ready oraz pierwszą odpowiedź DrawingAnswer. Pełne uruchomienie mixed-client wymaga jeszcze deterministycznego setupu Published package, pokoju oraz skryptowanych klientów SignalR w orkiestratorze.

Jest to dług infrastruktury E2E, a nie brak funkcjonalności produkcyjnej Etapu 6A. Zakres został przeniesiony do późniejszego hardeningu i nie blokuje odbioru Admin Content Editora.
