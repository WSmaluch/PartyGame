# Diagnostyka runtime

`GET /api/system/version` zwraca bezpieczny kontrakt: wersję aplikacji, commit, timestamp, środowisko oraz wspólną wersję API/SignalR, format backupu i support bundle'a. Nie zwraca ścieżek ani konfiguracji.

`GET /api/admin/diagnostics/summary` wymaga operator bearer tokenu. Zawiera readiness, schema database, rozmiary i liczność mediów, aktywne pokoje/połączenia, lifecycle backup/restore/migration oraz bezpieczne podsumowanie konfiguracji logów. Nie zawiera tokenów, adresów klientów, połączenia z bazą ani ścieżek.

`scripts/diagnose-lan.sh --deploy-root <dir> --base-url <url> --operator-token-env <name> --output <file>` wykonuje stabilne kontrole `PASS`, `WARN`, `FAIL`, nie wypisując tokenu.
