# Etap 6B.1–6B.4 — trwałe przechowywanie i cleanup mediów

## Cel i zakres

Etap 6B.1 wprowadza trwały, lokalny provider plikowy oraz metadane SQLite dla zdjęć profilu, odpowiedzi fotograficznych i odpowiedzi rysunkowych. Klient nigdy nie otrzymuje ścieżki systemu plików: operuje wyłącznie na istniejących adresach API i identyfikatorach assetów.

Zakres nie obejmuje dostawców chmurowych, CDN, signed URLs, wdrożenia produkcyjnego, retencji odpowiedzi do gry ani orkiestracji Mixed Client E2E.

## Przepływy mediów

| Rodzaj | Zapis | Rekord SQLite | Odczyt dla klientów |
| --- | --- | --- | --- |
| `ProfilePhoto` | `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo` | `MediaAsset`, a `Player.ProfilePhotoMediaAssetId` wskazuje aktualny asset | `GET /api/rooms/{roomCode}/players/{playerId}/profile-photo` |
| `PhotoAnswer` | istniejący endpoint odpowiedzi fotograficznej | `PhotoAnswerSubmission.MediaAssetId` i `MediaAsset` | `GET /api/media/{mediaAssetId}/display` lub `/thumbnail` |
| `DrawingAnswer` | istniejący endpoint odpowiedzi rysunkowej | `DrawingAnswerSubmission.MediaAssetId` i `MediaAsset` | `GET /api/media/{mediaAssetId}/display` lub `/thumbnail` |

Display Web i iOS nadal korzystają z tych samych kontraktów URL. Nie znają katalogu storage ani lokalnych ścieżek.

## Architektura

`IMediaStorage` jest neutralną abstrakcją infrastruktury. Przyjmuje strumień, deklarowany MIME type i kontekst zapisu, a zwraca opaque storage keys, wymiary, długość i SHA-256. Jedyny provider 6B.1, `LocalMediaStorage`, zapisuje dane pod konfigurowanym katalogiem trwałym. Ścieżka względna jest liczona względem katalogu aplikacji; w środowisku produkcyjnym powinna wskazywać wolumen trwały.

Każdy asset ma typ wyliczeniowy `MediaKind` (`ProfilePhoto`, `PhotoAnswer`, `DrawingAnswer`) oraz `RoomId`, `PlayerId` i, gdy dotyczy, `QuestionInstanceId`. Zapis profilu dostaje nowy UUID assetu przed zapisem, dlatego kolejne uploady nie mogą nadpisać starego pliku. Aktualny profil wskazuje `Player.ProfilePhotoMediaAssetId`.

Warianty `display` i `thumbnail` mają osobne opaque keys. Endpoint odczytu najpierw wyszukuje metadane w SQLite, a następnie otwiera klucz przez provider; nie przyjmuje ścieżki lokalnej od klienta.

## Bezpieczeństwo uploadu

Provider akceptuje wyłącznie JPEG/PNG dla zdjęć oraz PNG dla rysunków. Sprawdza limit rozmiaru, wymagane wymiary, magic bytes i rzeczywisty format odczytany przez ImageSharp. Usuwa EXIF/ICC/XMP/IPTC, normalizuje orientację oraz zapisuje własny JPEG albo PNG. Nazwy przesyłanych plików nie uczestniczą w budowaniu ścieżek. Resolver storage odrzuca klucze wychodzące poza root.

Zapisy odbywają się atomowo przez plik tymczasowy i przeniesienie do docelowej nazwy. Przy błędzie czyszczone są utworzone warianty. SHA-256 jest liczony ze zapisanego pliku strumieniowo.

## Konfiguracja

Sekcja `MediaStorage` w `server/PartyGame.Api/appsettings.json` ma domyślnie:

```json
{
  "Provider": "LocalFileSystem",
  "RootPath": "data/media",
  "ProfilePhotoMaximumUploadBytes": 5242880,
  "ProfilePhotoCleanupBatchSize": 25,
  "OrphanedGameMediaCleanupBatchSize": 25,
  "UntrackedFileCleanupEnabled": true,
  "UntrackedFileCleanupBatchSize": 25,
  "UntrackedFileCleanupGracePeriodMinutes": 60
}
```

`RootPath` nie może wskazywać katalogu tymczasowego procesu. Dla hostingu należy podać katalog na trwałym wolumenie i objąć go backupem zgodnie z polityką środowiska.

## Cleanup zastąpionych zdjęć profilu (6B.2)

Po pomyślnym zapisie nowych wariantów i zatwierdzeniu w SQLite przełączenia `Player.ProfilePhotoMediaAssetId`, aplikacja próbuje usunąć poprzedni asset typu `ProfilePhoto`. Przed każdym usunięciem wariantu oraz bezpośrednio przed usunięciem rekordu sprawdza ponownie, że żaden `Player` nie wskazuje assetu. Dzięki temu równoległe uploady nie usuwają aktualnego zdjęcia.

Cleanup usuwa kolejno warianty `display` i `thumbnail`, a dopiero potem rekord `MediaAsset`. Usunięcie nieistniejącego pliku jest idempotentne. Gdy usunięcie pliku lub rekordu się nie powiedzie, rekord pozostaje w SQLite do ponownej próby; udany upload nie jest cofany. Logi diagnostyczne zawierają identyfikator assetu i typ błędu, nigdy fizyczną ścieżkę pliku.

Przy starcie hosta wykonywana jest ograniczona do `ProfilePhoto` próba ponowienia dla nieużywanych assetów. Pobiera maksymalnie `ProfilePhotoCleanupBatchSize` kandydatów (domyślnie 25), stabilnie według `CreatedAtUtc`, a następnie `Id`. Nie obejmuje to `PhotoAnswer` ani `DrawingAnswer` i nie wprowadza migracji schematu.

Etap 6B.2 nie zmienia klientów iOS, Display ani Admin oraz nie zmienia ich kontraktów API.

## Cleanup osieroconych mediów gry (6B.3)

6B.3 nie jest retencją poprawnych odpowiedzi. Ograniczony startup batch kwalifikuje wyłącznie rekord `PhotoAnswer`, dla którego nie istnieje `PhotoAnswerSubmission`, albo rekord `DrawingAnswer`, dla którego nie istnieje `DrawingAnswerSubmission`. `ProfilePhoto` pozostaje obsługiwane wyłącznie przez cleanup 6B.2.

Batch pobiera maksymalnie `OrphanedGameMediaCleanupBatchSize` kandydatów (domyślnie 25), stabilnie według `CreatedAtUtc`, a następnie `Id`. Przed usunięciem każdego wariantu i rekordu ponownie sprawdzany jest typ oraz brak submission. Cleanup usuwa idempotentnie `display` i `thumbnail`, a rekord DB dopiero po sukcesie obu operacji. Błąd pozostawia rekord do kolejnego startup retry i jest logowany przez ID, rodzaj oraz typ błędu bez ścieżki fizycznej.

Mechanizm jest DB-led: nie skanuje finalnego filesystemu ani nie usuwa plików bez rekordu DB.

## Cleanup finalnych plików bez rekordu DB (6B.4)

6B.4 uzupełnia DB-led cleanup osobnym, filesystem-led reconcilerem dla lokalnego providera. Przy jednym starcie hosta wykonywany jest jeden batch starych finalnych plików, których względny storage key nie występuje ani jako `MediaAsset.DisplayStorageKey`, ani jako `MediaAsset.ThumbnailStorageKey`. Domyślny limit wynosi 25, jest ograniczony do zakresu 1–100, a domyślny grace period wynosi 60 minut.

Lokalna abstrakcja katalogu enumeruje wyłącznie dokładne struktury tworzone przez `LocalMediaStorage` pod drzewami `profile`, `photo-answer` i `drawing-answer`, z wariantami `display` i `thumbnail` oraz właściwym rozszerzeniem JPEG albo PNG. Nie rozszerza ogólnego `IMediaStorage` o enumerację filesystemu. Nie podąża za symlinkami i pozostawia nietknięte `.tmp`, ukryte pliki, nieznane nazwy, rozszerzenia, głębokości oraz katalogi.

Bezpośrednio przed usunięciem reconciler ponownie odczytuje timestamp pliku i wykonuje świeże zapytanie do SQLite dla obu kolumn storage key. Plik odświeżony w grace period albo wskazany w międzyczasie przez `MediaAsset` pozostaje. Brak pliku jest idempotentny; błąd pojedynczego delete nie zatrzymuje batcha, pozostawia plik do retry i jest logowany przez względny key oraz typ błędu bez fizycznego root path.

Reconciler jest przeznaczony dla obecnej topologii pojedynczego hosta i jednego lokalnego wolumenu. Nie zapewnia koordynacji wielu procesów współdzielących storage. 6B.4 nie zmienia API ani klientów iOS, Display i Admin.

## Trwałość i testy

Migracja `Stage6BPersistentMediaStorage` rozszerza `MediaAssets` o typ i kontekst oraz dodaje referencję bieżącego assetu profilu. Zachowane odpowiedzi z 6A są przypisywane do odpowiedniego pokoju, gracza i pytania podczas migracji.

Testy obejmują walidację formatu i magic bytes, normalizację, atomiczny provider lokalny, odczyt bez traversal, metadane typów oraz restart rzeczywistego hosta z tym samym SQLite i katalogiem mediów. Pełna lista wykonanych regresji jest raportowana razem z zamknięciem etapu.

Migracja SQLite może na czas kontrolowanego przebudowania tabel wyłączyć sprawdzanie kluczy obcych. Po migracji i podczas normalnej pracy połączenia obowiązuje `PRAGMA foreign_keys = 1`; odbiór migracji wymaga również `PRAGMA integrity_check = ok` i pustego wyniku `PRAGMA foreign_key_check`. Scenariusz Host A / Host B ma uruchamiać dwa kolejne hosty z tą samą bazą i tym samym storage root oraz potwierdzać dla wszystkich trzech rodzajów mediów MIME type, magic bytes, długość i SHA-256.

## Integracja DrawingAnswer na iOS

`GameRealtimeClient.swift` ani produkcyjny lifecycle `GameSessionStore` nie otrzymały w tym etapie osobnej maszyny stanów, reconnectu operacji ani obsługi błędu przez dopasowanie tekstu `Socket is not connected`. Submit odpowiedzi i głosów pozostaje pojedynczym działaniem użytkownika; błąd połączenia jest prezentowany użytkownikowi, a nie cicho ignorowany lub automatycznie ponawiany.

Skrypt `scripts/test-ios-drawing-answer-integration.sh` wykonuje `build-for-testing` przed uruchomieniem API. Następnie uruchamia API z unikalną bazą SQLite i storage root, czeka na health-check, uruchamia klienta demonstracyjnego i wykonuje `test-without-building`. Jednosekundowy `Task.sleep` występuje wyłącznie w `DrawingAnswerBackendIntegrationTests` bezpośrednio po świadomym `disconnect()`; jest cooldownem fixture symulatora, a nie produkcyjną naprawą lifecycle SignalR.

## Granice dalszych prac

Do formalnego domknięcia lokalnego storage 6B pozostaje storage-aware health i podstawowa diagnostyka pojemności. Retencja prawidłowo referencjonowanych odpowiedzi wymaga osobnej polityki lifecycle pokojów i nie jest częścią cleanupów 6B.2–6B.4.

Backup i restore, provider chmurowy, migracja między providerami, CDN, signed URLs, administracyjne zarządzanie mediami oraz produkcyjny deployment należą do późniejszych prac operations/deployment. Pełne Mixed Client E2E pozostaje osobnym hardeningiem infrastruktury testowej i nie jest częścią storage 6B.
