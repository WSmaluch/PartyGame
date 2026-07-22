# Etap 1B — klienci lobby

Klienci (iOS i aplikacja webowa Display) realizują w Etapie 1B kliencki interfejs do funkcji zaimplementowanych w Etapie 1A. Architektura bazuje na ścisłym oddzieleniu logiki biznesowej (backend) od prezentacji (klienci).

## Główne założenia

- Klienci (iOS, React) nie modyfikują i nie obliczają samodzielnie stanów gry, są jedynie interfejsem dla zdarzeń SignalR i poleceń REST.
- Każda publiczna zmiana zwiększa serwerowy `stateVersion`; klienci iOS i Display odrzucają starsze migawki.
- Surowy reconnect token jest generowany kryptograficznie. W iOS token należy przechowywać w Keychain i pod żadnym pozorem nie umieszczać go w logach ani w UserDefaults.

## Architektura Klientów

### iOS (`apps/ios`)
Zbudowany z użyciem SwiftUI i wzorca projektowego z zarządzaniem stanem przez obiekt obserwowalny `GameSessionStore` uruchamiany na `MainActor`.

- **Komunikacja**: `RoomAPIClient` (REST) oraz `SignalRGameRealtimeClient` (`dotnet/signalr-client-swift` przypięty do stabilnej wersji 1.0.0) odpowiedzialne za łączność.
- **Bezpieczeństwo**: Sesja i powiązany z nią `reconnectToken` są odseparowane, a token trafia bezpiecznie do wbudowanego modułu Keychain poprzez implementację `PlayerSessionStorage`.
- **Zdjęcia profilowe**: Z wykorzystaniem `AVFoundation` / `PhotosUI`. Procesor zdjęć (`ProfilePhotoProcessor`) skaluje obrazy zachowując proporcje do maksymalnego wymiaru 1200 px, poddaje kompresji JPEG dbając o rozmiar końcowy (do 5 MiB).

### Display Web (`apps/display-web`)
Napisany w technologii React 19 + TypeScript, serwowany i budowany przy pomocy Vite.

- **Obsługa WebSocketów**: Do obsługi zdarzeń w czasie rzeczywistym użyto `@microsoft/signalr`. Plik `gameHubConnection.ts` abstrahuje połączenie do instancji hubu.
- **Odświeżanie i uwierzytelnianie**: Ekran autoryzuje się jako ekran współdzielony metodą `AttachDisplay(roomCode)` (nie wymaga tokenu). Kod zapamiętywany jest lokalnie w `sessionStorage`.
- **Przypadki brzegowe**: Odbierane zdarzenie `DisplayReplaced` zatrzymuje dalszą obsługę pokoju dla bieżącej instancji (pokój "przejęty" przez inną kartę).

## Przykładowe ekrany klienta
1. **Host/Join**: Wpisanie kodu pokoju (Display) lub imienia. Kod normalizowany do wielkich liter bez znaków podobnych.
2. **Zdjęcie Profilowe**: Robienie, obróbka do docelowego formatu i przesłanie multipartem.
3. **Lobby**: Wyświetlanie innych graczy z bieżącymi zdjęciami; `stateVersion` na końcu URL zdjęć likwiduje problemy z agresywnym cache'owaniem przez przeglądarkę.
4. **Started (Gra rozpoczęta)**: Zdarzenie końcowe bieżącego etapu. Wyświetla statyczną wiadomość o sukcesie wejścia w fazę rozgrywki.

## Ograniczenia etapu

Nie ma jeszcze pytań, kategorii, rund, odpowiedzi, głosowania, punktacji, finału, panelu admina ani pełnego wznowienia trwającej gry po restarcie. Stan `Started` jedynie potwierdza spełnienie warunków lobby. Transport HTTP jest przeznaczony do developmentu w zaufanej sieci; wdrożenie wymaga HTTPS.
