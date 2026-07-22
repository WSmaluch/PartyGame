# Etap 5A — backend DrawingAnswer

`DrawingAnswer` jest czwartym typem pytania. Backend pozostaje jedynym źródłem prawdy i prowadzi przepływ `QuestionIntro → CollectingDrawingAnswers → RevealingDrawingAnswers → CollectingDrawingAnswerVotes → ShowingDrawingAnswerResults`. UI rysowania, upload i ekrany DrawingAnswer w klientach należą do Etapu 5B.

## PNG i storage

Klient wysyła finalny raster `image/png`. `LocalMediaStorage`, przez istniejące `IMediaStorage`, rzeczywiście dekoduje plik, sprawdza MIME i wymiary, normalizuje orientację, usuwa profile EXIF/XMP/ICC/IPTC, skaluje bez powiększania, zachowuje przezroczystość i zapisuje `display.png` oraz `thumbnail.png`. Zapis przechodzi przez plik `.tmp`, a SHA-256 jest liczony z finalnego wariantu display. Nazwa klienta nie tworzy ścieżki; klucze są generowane wyłącznie z identyfikatorów serwera i rozwiązywane wewnątrz `RootPath`.

Detektor tuszu kompozytuje półprzezroczyste piksele na skonfigurowanym tle i liczy udział pikseli różniących się od tła. Biały i przezroczysty canvas, pojedynczy piksel oraz ślady poniżej `MinimumInkPixelRatio` zwracają `drawing_answer_blank`; cienkie czarne, kolorowe i półprzezroczyste linie przechodzą.

## Prywatność i idempotencja

Multipart zawiera `playerId`, `reconnectToken`, UUID `clientSubmissionId` oraz `drawing`. To samo ID zwraca ten sam `drawingAnswerId` również po przejściu do reveal, bez nowych plików, rekordu i `stateVersion`. Nowe ID po zaakceptowanym uploadzie daje konflikt. Błąd bazy uruchamia kompensacyjne usunięcie obu finalnych wariantów.

Collecting publikuje tylko postęp. Reveal i voting publikują anonimowe ID, kontrolowane URL-e, wymiary oraz stabilną kolejność; autorzy, voterzy i punkty występują dopiero w results. `ownDrawingAnswerId` jest wyłącznie w `PlayerPrivateGameStateUpdated` aktywnego połączenia gracza. Self-vote jest dozwolony. Każdy voter otrzymuje `liczba głosów na wybrany rysunek × 100` z reason `DrawingAnswerConformity`; autor nie otrzymuje bonusu za popularność.

## Przypadki brzegowe i trwałość

Zero rysunków przechodzi bez reveal i voting do pustych wyników. Jeden rysunek jest anonimowo ujawniany, ale voting jest pomijany i nie powstaje ledger. Pauza Display zachowuje etap, pozostały czas, submissiony, `RevealOrder`, uprawnienia, głosy i score. Upload i głos podczas pauzy są odrzucane. SQLite i katalog media odtwarzają stan po restarcie; brak fizycznego pliku daje 404, nie destabilizując pokoju.

Migracja bazowa 5A ma nazwę `20260721190114_Stage5ADrawingAnswer`; komplet schematu i snapshotu domyka osobna migracja `20260721191126_Stage5ACompletionFix`. Seed dodaje stabilnymi kluczami 5 pytań na każdą z 10 kategorii, nie nadpisując ręcznych zmian.

## Ograniczenia

Storage pozostaje lokalny. Nie ma globalnego GC finalnych osieroconych plików ani moderacji. Canvas, stroke’i, narzędzia, undo/redo oraz ekrany iOS/Display nie są częścią 5A.
