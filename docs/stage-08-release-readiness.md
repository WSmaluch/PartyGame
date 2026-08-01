# Etap 8 — gotowość wydaniowa i uruchamianie w sieci LAN

Etap 8 przygotowuje powtarzalne artefakty i operacyjne zasady uruchomienia PartyGame. Nie zmienia reguł gry ani kontraktów klientów.

## Plan etapów

1. **8.1 — powtarzalny release build i konfiguracja środowiska.** Publikacja API, build webów i iOS, zewnętrzny runtime, manifest, checksumy, smoke test oraz wersjonowanie.
2. **8.2 — wdrożenie backendu i webów w LAN.** Konkretna topologia hosta, serwowanie Display/Admin i instrukcja instalacji w lokalnej sieci.
3. **8.3 — trwałość, migracje, backup i recovery.** Polityka wykonywania migracji, kopie zapasowe SQLite/mediów oraz ćwiczenie odtworzenia.
4. **8.4 — bezpieczeństwo, sekrety, CORS i ograniczenia sieciowe.** Uprawnienia procesu, źródła sekretów, firewall i produkcyjna polityka originów.
5. **8.5 — diagnostyka, logi, wersjonowanie i support bundle.** Retencja logów, format diagnostyki oraz pakiet wsparcia bez danych wrażliwych.
6. **8.6 — RC i końcowy test instalacyjny.** Powtarzalny scenariusz instalacji na czystym hoście i akceptacja release candidate.

## Status 8.1

W zakresie 8.1 API ma jawny kontrakt `PARTYGAME_*`, nie zapisuje danych runtime do katalogu publish w Production, a `/health/ready` sprawdza nieinwazyjnie bazę i katalog mediów. `scripts/build-release.sh` tworzy artefakt oraz uruchamia kontrolowany smoke test. Szczegółowa instrukcja jest w [local-release-build.md](deployment/local-release-build.md).

Polityka migracji i kopii zapasowych nie jest zamknięta w 8.1. `PARTYGAME_APPLY_MIGRATIONS=true` służy wyłącznie do jednorazowego, izolowanego smoke testu; reguły użycia w instalacji produkcyjnej należą do 8.3.

## Dowody walidacji 8.1

- Pełny Mixed Client E2E etapu 7: PASS; evidence: `/private/tmp/partygame-mixed-e2e-pass.hwwjRA`.
- Backend Release: 281/281 PASS. Display: lint, build i 36/36 testów PASS. Admin: lint, build i 80/80 testów PASS.
- iOS `Release build-for-testing`, release smoke, health/readiness, konfiguracja runtime, manifest i checksumy zostały zweryfikowane w przebiegu release 8.1.
- `git fsck --connectivity-only --no-dangling`: PASS. Odczyt wszystkich 865 osiągalnych obiektów przez `git cat-file`: PASS; nie wykryto `missing`, `corrupt`, `bad` ani `unknown`.
- Pełny `git fsck --full --no-dangling` nie wskazał błędu obiektu, ale lokalna próba zakończyła się komunikatem `mmap failed: Operation timed out`. To ograniczenie lokalnego filesystemu; szczegóły są w `/private/tmp/partygame-stage8-git-fsck.log`.
- `npm ci` zgłasza trzy podatności high. Nie wykonano automatycznego fixu; analiza i bezpieczna aktualizacja należą do 8.4.

**Etap 8.1 — ukończony.**

**Etap 8.2 — niewykonany.**

**Etap 8 — nieukończony.**
