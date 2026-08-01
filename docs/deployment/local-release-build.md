# Lokalny release build PartyGame

## Cel i granice

Ta instrukcja tworzy lokalny artefakt release oraz uruchamia go chwilowo w izolowanym runtime. Nie wdraża aplikacji do LAN i nie definiuje polityki backupu lub migracji produkcyjnych; to kolejne etapy 8.2 i 8.3.

## Wymagania

- .NET SDK zgodny z `global.json`, Node.js i npm z lockfile’ów, Xcode oraz narzędzia `git`, `curl` i `shasum`;
- booted iOS Simulator, którego UUID przekazujesz jako `IOS_DESTINATION_ID`;
- czyste drzewo Git. Lokalny eksperyment można oznaczyć jawnym `--allow-dirty`.

```bash
IOS_DESTINATION_ID="<simulator-uuid>" scripts/build-release.sh
```

Polecenie wykonuje restore i testy .NET, `npm ci` bez aktualizacji zależności, test/lint/build obu webów, iOS `Release build-for-testing`, publish API oraz smoke test. Artefakt trafia do `artifacts/release/<version>/`:

- `api/`, `display/`, `admin/` — publikowalne artefakty;
- `manifest.json`, `checksums.sha256`, `BUILD_INFO.txt` — identyfikacja builda i integralność.

Artefakt nie zawiera `node_modules`, DerivedData, runtimeowych DB/mediów, logów, danych testowych ani sekretów. Manifest nie zawiera ścieżek deweloperskich.

## Kontrakt środowiska API

Skopiuj wartości z rootowego `.env.example` do procesu uruchamiającego (plik nie jest automatycznie ładowany). W Production wymagane są:

| Zmienna | Znaczenie |
| --- | --- |
| `PARTYGAME_URLS` | Jawny adres nasłuchu, np. `http://0.0.0.0:5050`. |
| `PARTYGAME_DATABASE_PATH` | Ścieżka SQLite poza katalogiem publish. |
| `PARTYGAME_MEDIA_ROOT` | Katalog mediów poza katalogiem publish. |
| `PARTYGAME_PUBLIC_BASE_URL` | Publiczny adres API HTTP(S). |
| `PARTYGAME_ALLOWED_ORIGINS` | Lista dozwolonych originów rozdzielona przecinkami; bez `*`. |

Opcjonalne są `PARTYGAME_LOG_LEVEL`, `PARTYGAME_DISPLAY_PUBLIC_URL` i `PARTYGAME_ADMIN_PUBLIC_URL`. Nieprawidłowe, puste lub wildcardowe originy oraz dane runtime wewnątrz katalogu publish zatrzymują start czytelnym błędem. `appsettings.Production.json` jest wyłącznie szablonem bez sekretów; wartości mogą pochodzić także z konfiguracji ASP.NET.

## Konfiguracja artefaktów webowych

Display i Admin przy starcie pobierają `/config.json`. Musi zawierać `apiBaseUrl`, opcjonalny `signalRBaseUrl` (w przeciwnym razie API jest użyte dla SignalR), `publicAppUrl` oraz `buildVersion`. Brak lub błędna konfiguracja jest pokazany jako błąd uruchomienia — aplikacja nie przełącza się samoczynnie na `localhost`.

Skrypt release ustawia wersję w obu plikach `config.json`; adresy można podać podczas builda jako `PARTYGAME_PUBLIC_BASE_URL`, `PARTYGAME_DISPLAY_PUBLIC_URL` i `PARTYGAME_ADMIN_PUBLIC_URL`, albo uzupełnić w katalogu wdrożeniowym przed serwowaniem. Wersja jest też wypisywana w konsoli przeglądarki.

## Health i smoke

- `GET /health` to liveness i pozostaje lekki.
- `GET /health/ready` odróżnia gotowość bazy i katalogu mediów, bez zapisu testowych plików.
- `GET /api/system/version` zwraca bezpiecznie wersję, informational version, commit, timestamp i environment.

`scripts/smoke-release.sh artifacts/release/<version>` uruchamia opublikowany API w `Production`, z losowym lokalnym portem oraz zewnętrznym, tymczasowym DB/media. Sprawdza liveness, readiness, prosty endpoint API i zgodność wersji z manifestem. Proces jest śledzony przez PID; runtime jest kasowany przy sukcesie, a log zostaje tylko przy błędzie.

## iOS

8.1 sprawdza `Release build-for-testing`. Normalny Release nie korzysta z argumentów UI-testów; konfiguracja adresu serwera pozostaje zwykłym ustawieniem użytkownika. Odkrywanie hosta i scenariusz instalacji LAN nie należą jeszcze do tego etapu.

## Znane ograniczenia i ostrzeżenia

- Bieżące `npm ci` raportuje trzy podatności o poziomie high. Nie zastosowano automatycznego ani wymuszonego `npm audit fix`; analiza wpływu i bezpieczna aktualizacja zależności należą do etapu 8.4.
- Build może wypisać istniejące ostrzeżenia MSBuild dotyczące powtórnych importów oraz ostrzeżenie Xcode o nieużytym wyniku `try?` w `PartyGameApp.swift`. Nie blokują one artefaktu 8.1 i nie są zmianami zakresu release readiness.

## Stan etapu

**Etap 8.1 — ukończony.** Skrypt został zweryfikowany pełnym buildem Release i kontrolowanym smoke testem. Pełny Mixed Client E2E etapu 7 zakończył się PASS; evidence z tego przebiegu: `/private/tmp/partygame-mixed-e2e-pass.hwwjRA`. Orkiestrator zachował pełne outcome, ledger wersji stanu i kody procesów.

Walidacja obejmuje Backend Release (281/281), Display (36/36), Admin (80/80), iOS `Release build-for-testing`, health/readiness, konfigurację runtime, manifest, checksumy i smoke test. `git fsck --connectivity-only --no-dangling` oraz odczyt 865 osiągalnych obiektów przez `git cat-file` przeszły bez `missing`, `corrupt`, `bad` ani `unknown`. Pełny `git fsck --full --no-dangling` napotkał lokalne `mmap failed: Operation timed out`, bez komunikatu o uszkodzonym obiekcie; log diagnostyczny znajduje się w `/private/tmp/partygame-stage8-git-fsck.log`.

**Etap 8.2 — niewykonany. Etap 8 — nieukończony.** Instalacja LAN, produkcyjna polityka migracji/backupów oraz pozostałe punkty roadmapy nie są częścią tego artefaktu.
# Stabilność testów SQLite

Testy z ręcznie sterowanymi zegarami muszą wyłączyć wyłącznie własny, hosted `GameEngineWorker` przez długi interwał testowy. Dzięki temu worker nie zapisuje tej samej fazy co testowy `DbContext`; nie jest to globalne wyłączenie równoległości ani zmiana produkcyjnej konfiguracji. Każdy `WebApplicationFactory` używa unikalnego katalogu runtime i usuwa go dopiero po `DisposeAsync`, wraz z SQLite, WAL i SHM.
