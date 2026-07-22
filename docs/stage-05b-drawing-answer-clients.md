# Etap 5B — klienci DrawingAnswer

Etap 5B dodaje klienta SwiftUI i publiczne ekrany Display dla backendowego typu `DrawingAnswer`. Backend 5A pozostaje źródłem prawdy: klient nie nalicza punktów, nie prowadzi etapów i nie odkrywa autorów przed results.

## iOS

Lokalny draft jest kluczowany przez `roomCode + playerId + questionInstanceId`, zapisuje stroke'y, narzędzie, kolor, grubość, retry UUID oraz tymczasowy PNG w katalogu aplikacji. Jest usuwany po sukcesie, zmianie pytania, zakończeniu gry lub wyjściu z pokoju; reconnect go nie usuwa.

Canvas jest natywnym SwiftUI `Canvas`, ma pędzel, gumkę, paletę, trzy grubości, undo/redo i potwierdzane czyszczenie. Eksporter renderuje dopiero przy preview do PNG 1024×1024 z białym tłem. To świadoma decyzja: wygląd na Display jest stabilny, a gumka zapisuje kolor tła. Pusty draft jest blokowany lokalnie, ale backend nadal decyduje o `drawing_answer_blank`.

Upload używa multipart `playerId`, `reconnectToken`, `clientSubmissionId`, `drawing` z nazwą `drawing.png` i MIME `image/png`. Ten sam UUID jest zachowywany dla retry i reconnectu; zmiana stroke'ów generuje nowy UUID. Transfer 100% przechodzi do „zapisywania”, dopiero odpowiedź backendu oznacza sukces.

Prywatny stan jest filtrowany po graczu i `questionInstanceId`, a starsze snapshoty odrzuca `stateVersion`. `ownDrawingAnswerId` służy wyłącznie do oznaczenia własnego anonimowego rysunku w voting.

## Display

Display przechowuje wyłącznie publiczne DTO. Collecting pokazuje postęp bez obrazów. Reveal i voting renderują anonimowe PNG w trwałej kolejności. Results pokazuje autora, głosujących, punkty i wszystkie remisy z `isTopResult`.

PNG jest renderowany na białym tle karty. Błąd medium ukrywa uszkodzony obraz zamiast wywracać galerię. Po refreshie reveal pokazuje wszystkie aktualnie publiczne opcje — backend nie przekazuje czasu rozpoczęcia animacji reveal.

## Zakres poza 5B

Brak Apple Pencil pressure, warstw, kształtów, tekstu, zdjęć w płótnie, naklejek, filtrów, animowanych stroke'ów, backendowego zapisu stroke'ów, AI/moderacji, GCS, galerii systemowej i edytora Admin.

## Naprawione problemy w Etapie 5B

- **Flakiness testów (GameSessionStoreTests)**: Wycieki pamięci w postaci nieanulowanych zadań (`privateStateRefreshTask`). Zostało dodane anulowanie tasku oraz cykl czyszczenia w `tearDown`, co usunęło fatalne crashe.
- **Problem simctl**: Naprawiono wtórny błąd `simctl`, dostarczając odpowiednie środowisko Xcode w skryptach CI poprzez wymuszone `DEVELOPER_DIR` oraz właściwy `PATH` (posiadający ścieżkę .NET do kompilacji dem, potrzebną z kolei dla integracji C#-Swift).
- **Zero height Canvas**: `GeometryReader` powodował zgniatanie obszaru rysowania; poprawiono na użycie natywnych rozwiązań w SwiftUI co pozwala responsywnie ustawić canvas w równe proporcje 1:1.
- **Izolacja draftów**: Czyste canvasy od teraz zachowują deterministyczne odcięcie, co usunęło kolizje między stanami różnych sesji i pytań w asercjach testów integracyjnych i UI.
- **SignalR i LocalizedText**: Przerobiono logikę parsowania po stronie Swift. Najpierw dekodowany jest docelowy obiekt `[String: String]`, co unika starych rundek przez existential collections biblioteki SignalR.
- **Kontrakt postępu**: Dostosowanie iOS do kluczy z backendu: `submittedPlayers` i `requiredPlayers`.
- **E2E i Stress Testing**: Potwierdzone pełne przejścia z `Display E2E`, `Mixed E2E` na prawdziwym C# API, oraz brak flaky zachowań w stress przebiegach. Ukończone dema.
