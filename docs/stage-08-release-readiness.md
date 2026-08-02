# Etap 8 — gotowość wydaniowa i uruchamianie w sieci LAN

Etap 8 przygotowuje powtarzalne artefakty i operacyjne zasady uruchomienia PartyGame. Nie zmienia reguł gry ani kontraktów klientów.

## Plan etapów

1. **8.1 — powtarzalny release build i konfiguracja środowiska.** Publikacja API, build webów i iOS, zewnętrzny runtime, manifest, checksumy, smoke test oraz wersjonowanie.
2. **8.2 — wdrożenie backendu i webów w LAN.**
   - **8.2A:** układ wdrożenia i konfiguracja LAN;
   - **8.2B:** lifecycle start/stop/status/restart;
   - **8.2C:** statyczny Display i Admin bez Vite;
   - **8.2D:** walidacja z użyciem adresu LAN;
   - **8.2E:** rollback i ponowne wdrożenie bez utraty danych.
   - **8.2F:** stabilizacja walidacji SQLite i końcowa regresja release/LAN.
3. **8.3 — trwałość, migracje, backup i recovery.** Kontrolowane migracje, backup/verify/restore SQLite i mediów, rollback restore oraz retencja.
4. **8.4 — bezpieczeństwo, sekrety, CORS i ograniczenia sieciowe.** Uprawnienia procesu, źródła sekretów, firewall i produkcyjna polityka originów.
5. **8.5 — diagnostyka, logi, wersjonowanie i support bundle.** Retencja logów, format diagnostyki oraz pakiet wsparcia bez danych wrażliwych.
6. **8.6 — RC i końcowy test instalacyjny.** Powtarzalny scenariusz instalacji na czystym hoście i akceptacja release candidate.

## Status 8.1

W zakresie 8.1 API ma jawny kontrakt `PARTYGAME_*`, nie zapisuje danych runtime do katalogu publish w Production, a `/health/ready` sprawdza nieinwazyjnie bazę i katalog mediów. `scripts/build-release.sh` tworzy artefakt oraz uruchamia kontrolowany smoke test. Szczegółowa instrukcja jest w [local-release-build.md](deployment/local-release-build.md).

Produkcja startuje w trybie kontroli zgodności schematu. `PARTYGAME_APPLY_MIGRATIONS=true` pozostaje wyłącznie świadomym trybem operatora; standardowy deployment 8.3 wykonuje pre-migration backup i jawną migrację przed startem API. Szczegóły opisuje instrukcja danych.

## Dowody walidacji 8.1

- Pełny Mixed Client E2E etapu 7: PASS; evidence: `/private/tmp/partygame-mixed-e2e-pass.hwwjRA`.
- Backend Release: 281/281 PASS. Display: lint, build i 36/36 testów PASS. Admin: lint, build i 80/80 testów PASS.
- iOS `Release build-for-testing`, release smoke, health/readiness, konfiguracja runtime, manifest i checksumy zostały zweryfikowane w przebiegu release 8.1.
- `git fsck --connectivity-only --no-dangling`: PASS. Odczyt wszystkich 865 osiągalnych obiektów przez `git cat-file`: PASS; nie wykryto `missing`, `corrupt`, `bad` ani `unknown`.
- Pełny `git fsck --full --no-dangling` nie wskazał błędu obiektu, ale lokalna próba zakończyła się komunikatem `mmap failed: Operation timed out`. To ograniczenie lokalnego filesystemu; szczegóły są w `/private/tmp/partygame-stage8-git-fsck.log`.
- `npm ci` zgłasza trzy podatności high. Nie wykonano automatycznego fixu; analiza i bezpieczna aktualizacja należą do 8.4.

**Etap 8.1 — ukończony.**

**Etap 8.2 — ukończony i w pełni zwalidowany.** Jednoprocesowy deployment LAN używa opublikowanego API do serwowania `/display` i `/admin`, ma trwały katalog runtime, lifecycle, checksumy i rollback. Instrukcja: [lan-deployment.md](deployment/lan-deployment.md).

## Stabilizacja 8.2F

`PhotoAnswerMixedGameE2ETests` steruje przejściami faz bezpośrednio przez `GameStateMachine`. Wcześniej równolegle działał produkcyjny `GameEngineWorker` z domyślnym interwałem 1 s i mógł zapisać ten sam `GameSession` w SQLite. Powodowało to okazjonalny konflikt zapisu (`database is locked`) podczas pełnego przebiegu. Test ma teraz lokalny interwał workera 60 s oraz po akcjach HTTP/SignalR odświeża swój `DbContext` przed wymuszeniem kolejnego przejścia. Nie zmienia to semantyki produkcyjnego silnika.

`PhotoAnswerTestHarness` od początku nadaje każdemu hostowi własny katalog, plik SQLite i katalog mediów. Dodany test regresyjny potwierdza rozłączność i cleanup po `DisposeAsync`, obejmujący bazę oraz pliki WAL/SHM usuwane razem z katalogiem runtime.

Końcowa walidacja 8.2F: Backend Release 285/285 PASS; clean release build, manifest, SHA-256 i smoke PASS; pełna regresja deploymentu LAN (deploy/start/status/health/readiness/version/Display/Admin/SignalR/restart/redeploy/runtime preservation/rollback/stop) PASS przez rzeczywisty nie-loopbackowy adres hosta. Pełny Mixed Client E2E PASS: cztery różne typy pytań, `Completed`, ranking 3, dokładnie jedno `RoomStarted` i monotoniczny ledger `stateVersion`; evidence: `/private/tmp/partygame-mixed-e2e-pass.QfbQ5p`. To nie jest test na drugim fizycznym urządzeniu — ta walidacja pozostaje **manual validation pending**.

**Status:** 8.1 — ukończony; 8.2A–8.2F — ukończone; 8.2 — ukończony i w pełni zwalidowany; 8.3 — ukończony; 8.4A–8.4E — ukończone; 8.4 — ukończony; 8.5 — niewykonany; Etap 8 — nieukończony. Instrukcja: [data-backup-and-restore.md](deployment/data-backup-and-restore.md), [security hardening](security/security-hardening.md).

**Etap 8 — nieukończony.**
