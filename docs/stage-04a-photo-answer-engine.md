# Etap 4A — backend PhotoAnswer

Backend dodaje trzeci typ pytania i cztery trwałe etapy zdjęciowe. Lista uprawnionych jest zamrażana przy wejściu w zbieranie. Zero zdjęć prowadzi bezpośrednio do pustych wyników, jedno przechodzi przez anonimowy reveal bez głosowania, a co najmniej dwa uruchamiają głosowanie.

## Upload i media

Multipart upload jest autoryzowany reconnect tokenem i idempotentny przez UUID klienta. Mutacja działa pod blokadą pokoju. `LocalMediaStorage` dekoduje JPEG/PNG przez ImageSharp 3.1.x, wykonuje `AutoOrient`, usuwa EXIF/ICC/XMP/IPTC, skaluje bez powiększania, koduje `display.jpg` i `thumbnail.jpg`, a następnie liczy SHA-256 pliku display. Linia 3.1 została wybrana świadomie: aktualne ImageSharp 4 wymaga klucza licencyjnego dla bezpośredniej zależności; obowiązuje Six Labors Split License i przed komercjalizacją należy ponownie zweryfikować warunki na oficjalnej stronie Six Labors.

Domyślny root to `${TMPDIR}/PartyGame/media`; klucze mają postać `rooms/{roomId}/questions/{questionInstanceId}/{photoAnswerId}/{variant}.jpg`. Żadna wartość klienta nie staje się segmentem ścieżki. Storage odrzuca traversal i sprawdza zgodność zadeklarowanego MIME z faktycznie zdekodowanym formatem. Jeśli commit SQLite zawiedzie, oba finalne pliki są kompensacyjnie usuwane; nieudane usunięcie jest logowane bez ścieżki. Ograniczony sweep przy starcie usuwa tylko pliki `media/.tmp/*.tmp` starsze niż `MediaStorage:TemporaryFileRetentionMinutes` (domyślnie 60 minut) i nigdy nie przegląda finalnych mediów.

## Prywatność, wyniki i trwałość

Zbieranie nie ujawnia ID ani URL-i zdjęć. Reveal i głosowanie podają wyłącznie anonimowe opcje w trwałym `RevealOrder`. Wyniki ujawniają autorów i voterów. Każdy voter otrzymuje `liczba głosów na wybraną fotografię × 100` z ledgerem `PhotoAnswerConformity`; autor nie ma bonusu, ale może głosować na siebie.

Pauza Display korzysta z istniejącego `PausedStage` i pozostałego czasu. Encje, media keys, kolejność, głosy i ledger są trwałe, więc reconnect i restart odtwarzają stan. Brak pliku daje kontrolowane 404. `IMediaStorage` izoluje logikę gry od lokalnego filesystemu i pozwala później dodać provider Google Cloud Storage bez zmiany zasad gry.

## Odbiór Etapu 4A

Macierz integracyjna używa realnego hosta ASP.NET Core, multipart, SQLite i `LocalMediaStorage`. Obejmuje 30 warunków uploadu, ścieżki 0/1 zdjęcia, cztery etapy pauzy, restart dwóch instancji hosta na wspólnej bazie/storage, brak medium, kompensację błędu commit i równoległe wyścigi `Task.WhenAll`. Scenariusz mieszany tworzy przez publiczne API dokładny plan 2 × PlayerSelection, 2 × TextAnswer, 2 × PhotoAnswer i dochodzi do `Completed`; pełne demo zdjęciowe dodatkowo realizuje uploady i głosowanie dla czterech pytań.

Polecenia odbiorowe:

```bash
./scripts/demo-player-selection-game.sh
./scripts/demo-text-answer-game.sh
./scripts/demo-photo-answer-game.sh
./scripts/demo-mixed-three-types-game.sh
```

Znane ograniczenia: provider pozostaje lokalny, nie ma automatycznego sweepu finalnych osieroconych katalogów po awarii samej kompensacji, a Etap 4B (kamera/galeria i UI PhotoAnswer) nie jest częścią tego odbioru. Warunki licencji ImageSharp trzeba niezależnie zweryfikować przed komercjalizacją; niniejszy zapis nie jest poradą prawną.
