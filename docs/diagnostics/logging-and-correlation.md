# Logowanie i correlation ID

API przyjmuje tylko `X-Correlation-ID` o długości do 64 znaków, zawierający litery, cyfry, `.`, `_` lub `-`. Nieprawidłowa wartość jest zastępowana bezpiecznym identyfikatorem, który wraca w nagłówku odpowiedzi i jest dodawany do logów Serilog.

Konfiguracja środowiskowa: `PARTYGAME_LOG_LEVEL`, `PARTYGAME_LOG_DIRECTORY`, `PARTYGAME_LOG_FILE_SIZE_LIMIT_MB`, `PARTYGAME_LOG_RETAINED_FILE_COUNT` i `PARTYGAME_LOG_FORMAT` (`json` lub `text`). W deployment log root jest poza katalogiem release; sink rotuje dziennie i po rozmiarze, ograniczając liczbę plików. Ręczne porządkowanie wykonuje `scripts/prune-logs.sh --log-root <dir> --keep-files N --keep-days N --dry-run`.

Logi nie mogą zawierać tokenów, Authorization, cookie, request body ani treści odpowiedzi graczy. Zdarzenia SignalR identyfikują połączenie, rolę, pokój oraz correlation ID; worker loguje błąd pojedynczego pokoju i kontynuuje pozostałe pokoje.
