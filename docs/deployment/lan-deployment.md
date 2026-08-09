# Wdrożenie PartyGame w zaufanej sieci LAN

Etap 8.2 uruchamia PartyGame jako **jeden proces `PartyGame.Api` i jeden port HTTP**. API, SignalR oraz gotowe buildy Display i Admin mają wspólny origin:

```text
http://<LAN-IP>:5050/display/
http://<LAN-IP>:5050/admin/
http://<LAN-IP>:5050/api/
http://<LAN-IP>:5050/hubs/game
http://<LAN-IP>:5050/health
http://<LAN-IP>:5050/health/ready
```

HTTP jest dopuszczalne wyłącznie w prywatnej, zaufanej sieci LAN i wymaga `PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true`. Poza nią skonfiguruj standardowy HTTPS Kestrel oraz publiczny URL HTTPS. Panel `/admin/` wymaga tokenu operatora ustawionego wyłącznie po stronie procesu; token nie jest przekazywany do `config.json`. Szczegóły: [TLS i sieć](../security/tls-and-networking.md).

## Wymagania hosta

Host potrzebuje `dotnet` zgodnego z artefaktem, `bash`, `curl`, `node` (wyłącznie do odczytu manifestu), `shasum` i wolnego portu TCP 5050. Nie potrzebuje IDE, Node modules ani Vite. Zbuduj artefakt na maszynie budującej przez `scripts/build-release.sh`, a następnie przenieś cały katalog `artifacts/release/<version>` na host. Po starcie uruchom `scripts/diagnose-lan.sh`; logi i bundle opisuje [diagnostyka](../diagnostics/runtime-diagnostics.md).

## Instalacja

Wybierz prywatny IPv4 hosta. Gdy skrypt wykryje dokładnie jeden adres z zakresu `10/8`, `172.16/12` lub `192.168/16`, użyje go automatycznie. Przy kilku interfejsach podaj go jawnie:

```bash
scripts/deploy-lan.sh \
  --deploy-root "$HOME/PartyGame" \
  --release-dir "/path/to/0.8.1-..." \
  --host 192.168.1.50 --port 5050
```

`0.0.0.0` jest tylko adresem nasłuchiwania i nigdy nie może być publicznym URL-em. `127.0.0.1` nie jest poprawnym adresem LAN. Deployment sprawdza manifest i wszystkie SHA-256 przed zmianą `current`, tworzy `display/config.json` i `admin/config.json`, a w razie nieudanego smoke testu przywraca poprzednią wersję.

Webowe `config.json` zawierają `apiBaseUrl`, `signalRHubUrl`, `publicBaseUrl` i `applicationVersion`. Przy wspólnym originie używają ścieżek względnych (`/`, `/hubs/game`, `/display/`, `/admin/`); dlatego zmiana IP wymaga ponownego `deploy-lan.sh` z właściwym `--host`, lecz nie wymaga `npm build`.

## Katalogi i trwałość

```text
<deploy-root>/
├── releases/<version>/{api,display,admin,manifest.json,checksums.sha256,BUILD_INFO.txt}
├── current -> releases/<version>
├── runtime/{database,media,logs,pid,temp}
└── config/partygame.env
```

Baza SQLite, media, logi i PID są tylko w `runtime/`; ponowne wdrożenie nie usuwa ich. Katalog release jest po instalacji tylko do odczytu. `current` jest podmieniany atomowo. Konfiguracja webów jest jedynym deploymentowym plikiem podmienianym przed ustawieniem release jako niezmiennego i jest wyłączona z późniejszej kontroli jego checksumów; wszystkie pozostałe pliki są sprawdzane względem manifestu.

Lifecycle oraz narzędzia diagnostyczne używają tego samego resolvera `current`: akceptuje on względny lub absolutny symlink, kanonikalizuje go przez `pwd -P` (także na macOS, gdzie `/var` może wskazywać na `/private/var`), wymaga celu pod `<deploy-root>/releases/` i sprawdza minimalny layout release. Link uszkodzony, zwykły plik zamiast linku, escape poza `releases/` albo niekompletny release są bezpiecznie odrzucane.

## Lifecycle i logi

```bash
scripts/start-lan.sh   --deploy-root "$HOME/PartyGame"
scripts/status-lan.sh  --deploy-root "$HOME/PartyGame" # 0 ready, 1 stopped, 2 obcy PID, 3 readiness fail
scripts/restart-lan.sh --deploy-root "$HOME/PartyGame"
scripts/stop-lan.sh    --deploy-root "$HOME/PartyGame"
scripts/smoke-lan.sh   --deploy-root "$HOME/PartyGame"
```

Dodaj `--host` i `--port`, gdy różnią się od zapisanej konfiguracji. API jest uruchamiane z opublikowanego DLL, a stdout/stderr trafiają do `runtime/logs/`. PID jest zapisywany atomowo i zanim zostanie zatrzymany jest porównywany z pełną ścieżką DLL bieżącego release; skrypty nie używają `pkill`, `killall` ani globalnego wyszukiwania procesu.

## Aktualizacja i rollback

Deploy nowego artefaktu zachowuje runtime. Przepływ 8.3 to `backup → stop → deploy → migrate → start → readiness → smoke`; `deploy-lan.sh` wykonuje ten kontrolowany przebieg i blokuje rollback release, jeżeli aktualny schemat bazy jest niezgodny ze starszym API. Ręczne procedury backupu i recovery opisuje [data-backup-and-restore.md](data-backup-and-restore.md).

```bash
scripts/deploy-lan.sh --deploy-root "$HOME/PartyGame" --rollback <version> --host 192.168.1.50
```

Rollback weryfikuje release, zatrzymuje własny proces, atomowo przełącza `current`, uruchamia API i wykonuje readiness/smoke. Jeśli nowa wersja nie startuje, wraca do poprzedniego `current`; baza i media nie są modyfikowane przez sam mechanizm rollbacku.

## iOS i drugi komputer

W PartyGame iOS otwórz ustawienia serwera, wpisz `http://<LAN-IP>:5050` i użyj testu połączenia. Aplikacja zapisuje poprawny adres, usuwa zbędny końcowy `/` i raportuje błąd transportu lub niegotowego backendu. Nie ma automatycznego Bonjour/mDNS w 8.2.

Na drugim komputerze lub telefonie, bez uruchamiania lokalnego Vite, otwórz `/display/` oraz `/admin/`, następnie `health` i utwórz pokój. Sprawdź też, czy Display połączył się z SignalR. Nie twierdź, że test był między fizycznymi urządzeniami, jeśli wykonywano go tylko przez adres LAN z tego samego hosta.

macOS: zezwól `dotnet` na połączenia przychodzące w **Ustawienia systemowe → Sieć → Zapora sieciowa**. Linux: otwórz wybrany port TCP w lokalnym firewallu (np. `ufw allow 5050/tcp`) tylko dla zaufanej podsieci. Przy błędach sprawdź `runtime/logs`, `status-lan.sh`, zajęty port, izolację klientów Wi-Fi i VPN. Aby usunąć instalację bez danych, usuń wyłącznie wskazane katalogi w `releases/` i symlink `current`; zachowaj `runtime/` oraz `config/`.

Automatyczna regresja używa realnego nie-loopbackowego adresu hosta. Walidacja na drugim fizycznym urządzeniu pozostaje **manual validation pending**; automatyczny test LAN nie jest deklarowany jako jej substytut.

Przed update release wykonaj zweryfikowany backup danych. Procedura `backup → stop → deploy → migrate → start → readiness → smoke` oraz rollback przez pre-migration backup opisuje [backup i restore danych](data-backup-and-restore.md). Nie cofaj release do wersji nieobsługującej aktualnego schematu.

## Wynik regresji 8.2F

Końcowa regresja automatyczna 8.2F przeszła na świeżym artefakcie release i rzeczywistym nie-loopbackowym adresie hosta. Zweryfikowała deploy, start, status, health, readiness, version, Display, Admin, SignalR negotiate, restart, redeploy, zachowanie runtime, rollback, stop, brak osieroconych procesów oraz zwolnienie portu. Nie zapisuje lokalnego IP do repozytorium i nie zastępuje testu na drugim fizycznym urządzeniu, który pozostaje **manual validation pending**.
