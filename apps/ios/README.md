# PartyGame iOS

Aplikacja graczy w SwiftUI dla iOS 17+. Zawiera ekran startowy, bezpieczne placeholdery Host/Join, trwałą konfigurację adresu serwera oraz diagnostykę `GET /health` opartą na `URLSession` i async/await.

## Uruchomienie

Otwórz `PartyGame.xcodeproj` w Xcode, wybierz schemat `PartyGame` i symulator iOS 17 lub nowszy. Z terminala:

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  xcodebuild build -project PartyGame.xcodeproj -scheme PartyGame \
  -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO
```

Testy wymagają nazwy lub identyfikatora dostępnego symulatora:

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  xcodebuild test -project PartyGame.xcodeproj -scheme PartyGame \
  -destination 'platform=iOS Simulator,name=iPhone 17 Pro'
```

## Konfiguracja serwera

Domyślnie symulator używa `http://localhost:5050`. Na fizycznym iPhonie otwórz ustawienia z ikony koła zębatego i wpisz `http://ADRES_IP_MACA:5050`. Adres jest walidowany i zapisywany w `UserDefaults`.

`Info.plist` używa wąskiego `NSAllowsLocalNetworking`, bez globalnego `NSAllowsArbitraryLoads`. Lokalne HTTP jest przeznaczone wyłącznie do developmentu w zaufanej sieci.

SignalR używa pakietu `signalr-client-swift` i implementacji `GameRealtimeClient`. Klient obsługuje lobby, reconnect oraz prywatny stan i wysyłanie odpowiedzi dla czterech typów pytań.

Przy powtarzanych uruchomieniach CI/lokalnych można raz zbudować bundle testowy, a potem rozdzielić testy bez ponownej kompilacji:

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  xcodebuild build-for-testing -project PartyGame.xcodeproj -scheme PartyGame \
  -destination 'platform=iOS Simulator,name=iPhone 17 Pro' \
  -derivedDataPath /private/tmp/partygame-derived CODE_SIGNING_ALLOWED=NO

DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  xcodebuild test-without-building -project PartyGame.xcodeproj -scheme PartyGame \
  -destination 'platform=iOS Simulator,name=iPhone 17 Pro' \
  -derivedDataPath /private/tmp/partygame-derived CODE_SIGNING_ALLOWED=NO
```

## Deferred E2E hardening

Status: **Implemented partially — execution deferred**.

`MixedGameClientE2ETests` posiada prawdziwe interakcje iOS obejmujące Join, systemowy `PhotosPicker`, zapis profilu, Ready oraz pierwszą odpowiedź DrawingAnswer. Pełne uruchomienie mixed-client wymaga jeszcze deterministycznego setupu Published package, pokoju oraz skryptowanych klientów SignalR w orkiestratorze.

Jest to dług infrastruktury E2E, a nie brak funkcjonalności produkcyjnej Etapu 6A. Zakres został przeniesiony do późniejszego hardeningu i nie blokuje odbioru Admin Content Editora.
