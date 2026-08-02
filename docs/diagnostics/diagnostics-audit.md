# Audyt diagnostyki — etap 8.5

Przed etapem 8.5 API udostępniało bezpieczne endpointy health, readiness, storage i wersji, a deployment miał manifest, checksumy oraz backupy. Serilog zapisywał tylko na konsolę: nie było trwałej rotacji, retencji, correlation ID ani operatorowego podsumowania. SignalR i silnik gry miały pojedyncze logi, ale bez wspólnego kontraktu pól i bez bezpiecznego bundle'a.

Etap 8.5 wprowadza trwałe, rotowane logi JSON lub tekstowe, korelację HTTP, ustrukturyzowane zdarzenia SignalR/worker, kontrakt wersji oraz chronione podsumowanie operatora. Nie loguje reconnect tokenów, Authorization, cookies, danych odpowiedzi, rysunków ani zdjęć. Bundle nie zawiera bazy SQLite, jej sidecarów, mediów, danych graczy, ścieżek użytkownika ani sekretów.

Otwarte ograniczenie jest świadome: publiczny health/version pozostaje ograniczony do bezpiecznych informacji, a pełne podsumowanie wymaga operator bearer tokenu.
