# Dane trwałe, migracje, backup i odtwarzanie

## Audyt początkowy 8.3

PartyGame ma prawdziwą historię migracji EF Core w `PartyGame.Infrastructure/Persistence/Migrations`; bieżąca migracja jest zapisywana standardowo w `__EFMigrationsHistory`. Produkcyjny host nie używa `EnsureCreated`: w Development migracje są stosowane przy starcie, a w Production tylko po jawnym `ReleaseRuntime:ApplyMigrations=true`. Przed 8.3 nie istniał jawny check wersji, komenda migracyjna ani procedura backupu/restore.

SQLite jest wybierane przez `ConnectionStrings:PartyGame`; w deployment LAN plik znajduje się poza release w `runtime/database/partygame.db`. SQLite może używać WAL, dlatego zwykłe kopiowanie samego pliku `.db` nie jest bezpiecznym backupem. Media są poza release w `runtime/media`; rekordy `MediaAsset` zawierają stabilne GUID-y, względne opaque storage keys, MIME, hash i wymiary — nie ma w nich ścieżek absolutnych. Rekord może przeżyć utratę pliku, a plik może przeżyć rekord; istniejące reconcileery to raportują/naprawiają ograniczonym zakresem, lecz nie tworzą backupu.

Trwałe dane obejmują pokoje, graczy i sesje, historię gry, pakiety treści, submission audit oraz rekordy i pliki mediów. Runtimeowe PID-y, locki, logi, katalogi tymczasowe, artefakty developerskie i `node_modules` nie są danymi backupu. Backup 8.3 używa spójnego snapshotu SQLite i manifestu mediów; restore operuje wyłącznie na zweryfikowanych backupach oraz atomowej podmianie katalogów.

## Migracje i zgodność

`GET /api/system/schema` zwraca bezpiecznie aktualną, najnowszą i minimalną obsługiwaną migrację EF Core, flagę `migrationRequired` oraz `databaseCompatibility`. Nie zwraca connection stringa ani ścieżek. Production domyślnie wykonuje tylko check; niezgodna lub wymagająca migracji baza blokuje start i readiness. Development pozostaje wygodny i stosuje migracje przy starcie. Kontrolowane tryby operatora to `scripts/migrate-data.sh --check`, `--migrate` oraz jawny, opt-in `--migrate-on-start`.

`--migrate` najpierw tworzy maintenance pre-migration backup, a dopiero potem stosuje EF Core. Nowsza, nieznana migracja jest niekompatybilna i nie jest cofana automatycznie.

## Backup

```text
<backup-root>/<timestamp>-<version>/
├── database/partygame.db
├── media/
├── backup-manifest.json
├── checksums.sha256
└── BACKUP_INFO.txt
```

`scripts/backup-data.sh --deploy-root PATH --backup-root PATH [--name NAME] [--online|--maintenance]` używa SQLite `.backup` (online backup API, bez zwykłego `cp` pliku WAL), potem `PRAGMA integrity_check`. Media są kopiowane wyłącznie według względnych kluczy zapisanych w snapshotcie SQLite; brak pliku, nadmiarowy plik, symlink lub zmiana hash podczas kopiowania przerywają operację. Backup jest budowany w ukrytym katalogu staging i publikowany atomowym `mv` dopiero po weryfikacji SHA-256.

Manifest wersji 1 zawiera czas, wersję aplikacji/commit, wersję schematu, rozmiary i liczbę mediów, identyfikator runtime, tryb, wynik integrity check i mapę checksumów. Nie zawiera ścieżek absolutnych.

## Verify, restore i rollback

`scripts/verify-backup.sh BACKUP_DIRECTORY` sprawdza strukturę, format manifestu, brak symlinków, checksumy, SQLite `integrity_check`, zgodność wersji schematu i liczbę mediów. Kody: `0` poprawny, `20` niekompletny, `21` checksum mismatch, `22` SQLite corrupt, `23` nieobsługiwany format, `24` nieobsługiwany schemat, `75` konflikt lock/procesu.

`scripts/restore-data.sh --deploy-root PATH --backup PATH [--backup-root PATH] [--dry-run]` odmawia przy działającym API. `--dry-run` sprawdza backup, schemat i miejsce bez zmiany danych lub procesu. Pełny restore tworzy najpierw pre-restore backup, kopiuje dane do runtime staging, ponownie sprawdza SQLite, a następnie podmienia jednocześnie bazę i media. Błąd podczas podmiany automatycznie przywraca poprzednią bazę i katalog mediów.

## Lock, miejsce i retencja

Backup, migrate i restore używają jednego atomowo tworzonego katalogu lock z metadanymi `operation`, `pid`, `startedAtUtc` i `applicationVersion`. Aktywny PID blokuje drugą operację; lock osierocony po nieistniejącym PID jest kontrolowanie odzyskiwany. Przed zapisem skrypty szacują bazę, media, staging i pre-restore kopię z marginesem; brak miejsca kończy operację przed modyfikacją danych.

`scripts/prune-backups.sh --backup-root PATH [--deploy-root PATH] --keep-last N --keep-days N [--dry-run]` usuwa tylko katalogi, które przeszły `verify-backup`; zachowuje ostatnie N oraz backupy młodsze niż N dni. Nie podąża za symlinkami ani nie usuwa ostatniego poprawnego backupu. Gdy podano deployment, aktywny lock restore/migracji blokuje retencję.

## Disaster recovery i update LAN

Kontrolowany update: `backup → stop → deploy release → migrate → start → readiness → smoke`. Przy błędzie: `stop → restore pre-migration backup → switch previous release → start → readiness`. Automatyczny downgrade schematu nie jest obsługiwany; rollback release z nowszym schematem należy zablokować przez endpoint schematu.

## Kontrakt operacyjny 8.3

Skrypty w `scripts/` wprowadzają tryby `check`, `migrate` i `migrate-on-start`, wspólną atomową blokadę danych, online snapshot SQLite, walidację manifestu/checksumów oraz restore z pre-restore backupem i rollbackiem.

`scripts/test-data-lifecycle.sh` jest testem integracyjnym w katalogu tymczasowym: uruchamia aktualne migracje, wymusza WAL, tworzy fixture photo/drawing, wykonuje backup online, verify, dry-run, pełne restore, wykrycie checksum mismatch, konflikt locka oraz dry-run i rzeczywistą retencję. Nie używa danych operatora.
