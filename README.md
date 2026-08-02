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
curl http://localhost:5050/health/storage
open http://localhost:5050/swagger
```

Hub SignalR jest dostępny pod `/hubs/game`; metoda diagnostyczna `Ping` zwraca `status: pong` i czas UTC serwera. REST lobby zaczyna się od `POST /api/rooms`. Pełny scenariusz i przykłady `curl` opisuje [Etap 1A i 1B](docs/stage-01-client-lobby.md).

Wszystkie media — ProfilePhoto, PhotoAnswer i DrawingAnswer — korzystają z trwałego `IMediaStorage`. Domyślna konfiguracja `MediaStorage:RootPath` to `data/media`, liczona względem katalogu aplikacji; w hostingu należy wskazać trwały wolumen. Runtime media są ignorowane przez Git. SQLite przechowuje opaque klucze, MIME type, hash, wymiary i kontekst pokoju/gracza, nigdy absolutne ścieżki.

Upload waliduje limit, magic bytes i rzeczywisty format obrazu, usuwa metadane i zapisuje atomowo z kompensacją błędu. Odpowiedzi zdjęciowe oraz profile są normalizowane do JPEG, a DrawingAnswer zachowuje PNG i odrzuca pusty canvas. Media są udostępniane wyłącznie przez `/api/media/{mediaAssetId}/{variant}` albo istniejący endpoint zdjęcia profilu. Szczegóły: [Etap 6B.1](docs/stage-06b-media-storage.md).

`GET /health/storage` wykonuje na żądanie bezpieczną diagnostykę wyłącznie lokalnego providera: krótki write/read/delete probe, pojemność wolumenu oraz liczniki rekordów i rozpoznanych finalnych plików. Wynik ma status `Healthy`, `Degraded` lub `Unhealthy`; domyślne progi wolnego miejsca to odpowiednio 10% (warning) i 5% (critical), a pomiar jest cache’owany przez 30 sekund. Odpowiedź nie zawiera ścieżek, storage keys ani identyfikatorów użytkowników. Wolumen trwały, backup i monitoring pozostają odpowiedzialnością deploymentu/operations.

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

W trybie deweloperskim obie aplikacje odczytują jawnie podane `VITE_API_BASE_URL` (np. z `.env.development`). Artefakty release pobierają konfigurację runtime z `/config.json`; brak konfiguracji nie powoduje fallbacku do `localhost`.

Aplikację iOS uruchom z [projektu Xcode](apps/ios/PartyGame.xcodeproj). W symulatorze `localhost` oznacza Maca i działa z lokalnym backendem. Na fizycznym iPhonie `localhost` oznacza telefon, dlatego w ekranie ustawień trzeba wpisać `http://ADRES_IP_MACA:5050`. Ustawienie waliduje URL, normalizuje końcowy `/`, jest zapisane lokalnie i przyciskiem połączenia sprawdza `/health` (w tym wersję API).

## Release build (Etap 8.1)

Powtarzalny lokalny artefakt Release utworzysz na czystym drzewie Git:

```bash
IOS_DESTINATION_ID="<simulator-uuid>" scripts/build-release.sh
```

Skrypt publikuje API, buduje Display, Admin i iOS (`Release build-for-testing`), wykonuje testy oraz izolowany smoke test. Wynik trafia do `artifacts/release/<version>/` wraz z `manifest.json`, `checksums.sha256` i `BUILD_INFO.txt`; artefakt nie zawiera danych runtime ani sekretów. Sprawdzenie integralności:

```bash
cd artifacts/release/<version>
shasum -a 256 -c checksums.sha256
```

Production wymaga jawnych `PARTYGAME_URLS`, `PARTYGAME_DATABASE_PATH`, `PARTYGAME_MEDIA_ROOT`, `PARTYGAME_PUBLIC_BASE_URL` i `PARTYGAME_ALLOWED_ORIGINS`; DB oraz media muszą znajdować się poza katalogiem publish. Display i Admin wymagają poprawnego `config.json`, a iOS nadal otrzymuje adres backendu z normalnych ustawień aplikacji. Endpointy operacyjne to `/health`, `/health/ready` i `/api/system/version`.

## Bezpieczeństwo wdrożenia (Etap 8.4)

W Production panel Admin wymaga `PARTYGAME_OPERATOR_TOKEN` (minimum 32 znaki, bez placeholdera) przesyłanego wyłącznie jako Bearer token. Admin pyta o niego po otwarciu strony i zachowuje go tylko w pamięci. Produkcyjny HTTP wymaga jawnego `PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true`; poza zaufaną LAN używaj HTTPS. Pełna polityka: [security hardening](docs/security/security-hardening.md), [TLS i sieć](docs/security/tls-and-networking.md), [audit zależności](docs/security/dependency-audit.md). Przed wydaniem uruchom `scripts/scan-secrets.sh --tracked` oraz `scripts/security-smoke.sh`.

Trwałe dane produkcyjne obsługują jawny check/migrate schematu EF Core oraz zweryfikowany backup/restore SQLite i mediów. Instrukcja operatora, kody wyjścia, dry-run, pre-restore backup i retencja: [backup i restore danych](docs/deployment/data-backup-and-restore.md).

Pełna instrukcja kontraktu środowiska, konfiguracji webów i smoke testu znajduje się w [docs/deployment/local-release-build.md](docs/deployment/local-release-build.md). Gotowy artefakt można wdrożyć w LAN bez Node, Vite i IDE przez [instrukcję LAN](docs/deployment/lan-deployment.md), a backup, restore i retencję opisuje [instrukcja danych](docs/deployment/data-backup-and-restore.md). Etapy 8.1, 8.2 i 8.3 są ukończone; Etap 8 jako całość pozostaje nieukończony do wykonania 8.4.

Walidacja 8.2F ustabilizowała `PhotoAnswerMixedGameE2ETests`: testowy worker nie ściga się już z ręcznie sterowanym przejściem SQLite, a każda fixture ma własny katalog runtime, bazę, media i cleanup po `DisposeAsync` (w tym WAL/SHM). Pełny backend Release (285/285), clean release build, regresja lifecycle LAN oraz pełny Mixed Client E2E przeszły. Evidence pełnego E2E: `/private/tmp/partygame-mixed-e2e-pass.QfbQ5p` (4 typy pytań, `Completed`, ranking 3, jedno `RoomStarted`, monotoniczny ledger wersji). Automatyczna regresja używa nie-loopbackowego adresu hosta, lecz test na drugim fizycznym urządzeniu pozostaje **manual validation pending**.

Walidacja 8.1 obejmuje Mixed Client E2E PASS (evidence: `/private/tmp/partygame-mixed-e2e-pass.hwwjRA`), Backend Release 281/281, Display 36/36, Admin 80/80, iOS `Release build-for-testing`, smoke, manifest i checksumy. Kontrola Git przeszła przez `fsck --connectivity-only` oraz odczyt 865 osiągalnych obiektów; pełny fsck napotkał lokalny timeout `mmap`, bez komunikatu o brakującym lub uszkodzonym obiekcie. Trzy znane podatności npm high są udokumentowane i nie zostały automatycznie naprawione.

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
