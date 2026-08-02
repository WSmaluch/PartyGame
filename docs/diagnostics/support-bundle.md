# Support bundle

Tworzenie: `scripts/create-support-bundle.sh --deploy-root <dir> --mode minimal|standard|extended`. Skrypt buduje tymczasowy katalog, redaguje wejście, weryfikuje je i atomowo publikuje archiwum. `scripts/verify-support-bundle.sh --bundle <file>` sprawdza strukturę, manifest, checksumy, escape przez symlink/traversal, zakazane bazy/media i secret scan.

Minimal zawiera wersję, health/readiness i ograniczony fragment logu. Standard zawiera dozwoloną konfigurację, metadane deployment/backup i ograniczone zredagowane logi. Extended zwiększa jedynie limity logów; nie istnieje tryb raw. Manifest zapisuje format, zakres, pominięcia i informację o truncation.

Admin może utworzyć, sprawdzić status i pobrać backendowy ZIP. Serwer ustala nazwę, utrzymuje pojedynczą operację i czyści stare pliki. Bundle nigdy nie zawiera bazy, WAL/SHM, mediów, tokenów, cookies, danych graczy ani pełnych ścieżek.
