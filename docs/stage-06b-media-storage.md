# Etap 6B.1 — trwałe przechowywanie mediów

## Cel i zakres

Etap 6B.1 wprowadza trwały, lokalny provider plikowy oraz metadane SQLite dla zdjęć profilu, odpowiedzi fotograficznych i odpowiedzi rysunkowych. Klient nigdy nie otrzymuje ścieżki systemu plików: operuje wyłącznie na istniejących adresach API i identyfikatorach assetów.

Zakres nie obejmuje dostawców chmurowych, CDN, signed URLs, wdrożenia produkcyjnego, retencji starych plików ani orkiestracji Mixed Client E2E.

## Przepływy mediów

| Rodzaj | Zapis | Rekord SQLite | Odczyt dla klientów |
| --- | --- | --- | --- |
| `ProfilePhoto` | `POST /api/rooms/{roomCode}/players/{playerId}/profile-photo` | `MediaAsset`, a `Player.ProfilePhotoMediaAssetId` wskazuje aktualny asset | `GET /api/rooms/{roomCode}/players/{playerId}/profile-photo` |
| `PhotoAnswer` | istniejący endpoint odpowiedzi fotograficznej | `PhotoAnswerSubmission.MediaAssetId` i `MediaAsset` | `GET /api/media/{mediaAssetId}/display` lub `/thumbnail` |
| `DrawingAnswer` | istniejący endpoint odpowiedzi rysunkowej | `DrawingAnswerSubmission.MediaAssetId` i `MediaAsset` | `GET /api/media/{mediaAssetId}/display` lub `/thumbnail` |

Display Web i iOS nadal korzystają z tych samych kontraktów URL. Nie znają katalogu storage ani lokalnych ścieżek.

## Architektura

`IMediaStorage` jest neutralną abstrakcją infrastruktury. Przyjmuje strumień, deklarowany MIME type i kontekst zapisu, a zwraca opaque storage keys, wymiary, długość i SHA-256. Jedyny provider 6B.1, `LocalMediaStorage`, zapisuje dane pod konfigurowanym katalogiem trwałym. Ścieżka względna jest liczona względem katalogu aplikacji; w środowisku produkcyjnym powinna wskazywać wolumen trwały.

Każdy asset ma typ wyliczeniowy `MediaKind` (`ProfilePhoto`, `PhotoAnswer`, `DrawingAnswer`) oraz `RoomId`, `PlayerId` i, gdy dotyczy, `QuestionInstanceId`. Zapis profilu dostaje nowy UUID assetu przed zapisem, dlatego kolejne uploady nie mogą nadpisać starego pliku. Aktualny profil wskazuje `Player.ProfilePhotoMediaAssetId`; stare assety pozostają do przyszłej polityki retencji.

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
  "ProfilePhotoMaximumUploadBytes": 5242880
}
```

`RootPath` nie może wskazywać katalogu tymczasowego procesu. Dla hostingu należy podać katalog na trwałym wolumenie i objąć go backupem zgodnie z polityką środowiska.

## Trwałość i testy

Migracja `Stage6BPersistentMediaStorage` rozszerza `MediaAssets` o typ i kontekst oraz dodaje referencję bieżącego assetu profilu. Zachowane odpowiedzi z 6A są przypisywane do odpowiedniego pokoju, gracza i pytania podczas migracji.

Testy obejmują walidację formatu i magic bytes, normalizację, atomiczny provider lokalny, odczyt bez traversal, metadane typów oraz restart rzeczywistego hosta z tym samym SQLite i katalogiem mediów. Pełna lista wykonanych regresji jest raportowana razem z zamknięciem etapu.

Migracja SQLite może na czas kontrolowanego przebudowania tabel wyłączyć sprawdzanie kluczy obcych. Po migracji i podczas normalnej pracy połączenia obowiązuje `PRAGMA foreign_keys = 1`; odbiór migracji wymaga również `PRAGMA integrity_check = ok` i pustego wyniku `PRAGMA foreign_key_check`. Scenariusz Host A / Host B ma uruchamiać dwa kolejne hosty z tą samą bazą i tym samym storage root oraz potwierdzać dla wszystkich trzech rodzajów mediów MIME type, magic bytes, długość i SHA-256.

## Integracja DrawingAnswer na iOS

`GameRealtimeClient.swift` ani produkcyjny lifecycle `GameSessionStore` nie otrzymały w tym etapie osobnej maszyny stanów, reconnectu operacji ani obsługi błędu przez dopasowanie tekstu `Socket is not connected`. Submit odpowiedzi i głosów pozostaje pojedynczym działaniem użytkownika; błąd połączenia jest prezentowany użytkownikowi, a nie cicho ignorowany lub automatycznie ponawiany.

Skrypt `scripts/test-ios-drawing-answer-integration.sh` wykonuje `build-for-testing` przed uruchomieniem API. Następnie uruchamia API z unikalną bazą SQLite i storage root, czeka na health-check, uruchamia klienta demonstracyjnego i wykonuje `test-without-building`. Jednosekundowy `Task.sleep` występuje wyłącznie w `DrawingAnswerBackendIntegrationTests` bezpośrednio po świadomym `disconnect()`; jest cooldownem fixture symulatora, a nie produkcyjną naprawą lifecycle SignalR.

## Kolejne części 6B

Do następnych etapów pozostają provider chmurowy i migracja między providerami, CDN/signed URLs, administracyjne zarządzanie mediami, polityka retencji i usuwania osieroconych plików oraz pełne Mixed Client E2E hardening.
