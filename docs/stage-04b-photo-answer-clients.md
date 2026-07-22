# Etap 4B — klienci PhotoAnswer

Etap 4B dodaje pełną prezentację i obsługę odpowiedzi zdjęciowych w aplikacji iOS oraz na Display. Backend Etapu 4A pozostaje jedynym źródłem prawdy: klient nie zmienia etapu, `stateVersion`, punktów ani autorstwa.

## iOS

Router mapuje `CollectingPhotoAnswers` na bezpieczny loader prywatnego stanu, ekran wyboru lub oczekiwanie po zapisie; pozostałe etapy na oczekiwanie reveal, anonimowe głosowanie, oczekiwanie po głosie i wyniki. Decyzja o pokazaniu aparatu zależy od publicznego `questionInstanceId` i prywatnych `hasSubmittedPhotoAnswer`/`hasSubmittedPhotoAnswerVote`. Event dla innego gracza lub pytania jest odrzucany, a wartości `true` nie cofają się dla tej samej instancji.

Aparat jest otwierany dopiero po akcji użytkownika i używa tylnej kamery w `UIImagePickerController`. Obsłużone są wszystkie statusy uprawnienia, brak kamery na symulatorze, przejście do ustawień i fallback do `PhotosPicker`, który nie wymaga pełnego dostępu do biblioteki. Opisy kamery są lokalizowane w `InfoPlist.strings` po polsku i angielsku.

Zdjęcie z aparatu, JPEG, PNG lub HEIC dekodowalne przez UIKit jest poza `MainActor` przerysowywane z poprawną orientacją, skalowane bez powiększania do maksymalnego boku 2048 px i ponownie kodowane jako JPEG (0,85). Ponowne kodowanie usuwa EXIF i GPS. Preview wymaga jawnego zatwierdzenia.

Dla draftu powstaje jeden `clientSubmissionId`. Dane retry są zapisane w tymczasowym pliku pod kluczem logicznym `roomCode + playerId + questionInstanceId`; zmiana zdjęcia tworzy nowy UUID, a błąd sieci i reconnect zachowują poprzedni. Stare pliki są czyszczone, pełny JPEG usuwany po potwierdzeniu backendu, a mały preview może pozostać w pamięci ekranu oczekiwania.

Upload używa `URLSession.upload`, 45-sekundowego timeoutu i multipart pól `playerId`, `reconnectToken`, `clientSubmissionId`, `photo` (`image/jpeg`, `photo.jpg`). UI rozróżnia przygotowanie, transfer z procentem, przetwarzanie po osiągnięciu 100%, zapis i błąd. Sukces następuje wyłącznie po odpowiedzi backendu; odpowiedź starego pytania jest ignorowana. Konflikt already-submitted i błędy wskazujące stary etap uruchamiają resume publicznego i prywatnego stanu. Token ani binarne body nie są logowane.

Galeria głosowania używa miniaturek i stabilnej kolejności. `ownPhotoAnswerId` służy wyłącznie do etykiety „Twoje zdjęcie”; self-vote jest dozwolony. Pierwsze dotknięcie zaznacza kartę, ponowne otwiera wariant display. Drugi głos blokuje prywatny stan i stan wysyłania. Wyniki pokazują dokładnie backendowe zdjęcia, autorów z awatarami, głosujących, `pointsAwarded`, wszystkie remisy `isTopResult`, a także przypadki z zerem i jednym zdjęciem.

Lekki aktor cache’uje obrazy w pamięci, współdzieli request tego samego URL, respektuje anulowanie widoku i czyści całość przy zmianie pytania. Nic nie jest zapisywane do biblioteki użytkownika. Pauza zachowuje draft, UUID i wybór głosu, ale router nie udostępnia akcji. Timer jest wizualny; przy zerze store odrzuca upload i głos oraz pokazuje oczekiwanie na serwer.

Widoki wspierają Dynamic Type, VoiceOver, Safe Area, małe ekrany i pionowy scroll. Zaznaczenie ma obramowanie, ikonę oraz opis, więc nie zależy wyłącznie od koloru. Postęp jest odczytywany procentowo. Wszystkie teksty PhotoAnswer i komunikaty błędów mają tłumaczenia PL/EN.

## Display

Publiczny model zawiera wyłącznie rzeczywiste DTO `photoAnswerResults`, `anonymousOptions` i `options`; nie zawiera prywatnego stanu gracza. Routing obejmuje collecting, reveal, voting i results oraz respektuje istniejące filtrowanie rosnącego `stateVersion`.

Collecting pokazuje zadanie, timer, licznik i statusy graczy, ale nie renderuje żadnego obrazu ani URL odpowiedzi. Reveal i voting sortują anonimowe opcje po `displayOrder`; voting używa miniaturek i nie pokazuje autorów, własności, głosów ani punktów. Results używa wariantu display, ujawnia awatary autora i głosujących, dokładne `pointsAwarded` oraz wszystkie `isTopResult`.

Obrazy zachowują proporcje przez `object-fit: contain`, więc pion, poziom i kwadrat mieszczą się bez rozciągania. Awaria pojedynczego medium daje neutralny fallback i nie blokuje galerii. Reveal wstępnie ładuje pierwsze obrazy, a efekt jest wyłączany przez Reduce Motion. Zmiana komponentu/pytania anuluje preload.

Backend nie publikuje czasu rozpoczęcia reveal. Dlatego po refreshu Display nie zgaduje pozycji animacji: pokazuje od razu wszystkie aktualnie publiczne anonimowe opcje, co zachowuje anonimowość i nie cofa reveal. `AttachDisplay`, REST snapshot, reconnect oraz `DisplayReplaced` pozostają obsługiwane przez istniejącą warstwę strony.

## Walidacja i ograniczenia

Fixture mode iOS pokrywa 17 ekranowych scenariuszy, ale jest jawnie oddzielony od integracji sieciowej. Symulator może oferować wirtualną kamerę; deterministyczny fixture braku kamery służy wyłącznie testowi fallbacku. Rzeczywiste obrazy w automatyzacji są generowanymi, pozbawionymi danych prywatnych fixture’ami. Storage chmurowy, moderacja, filtry, udostępnianie i pozostałe elementy Etapu 5 nie wchodzą w zakres.

Rzeczywistą integrację produkcyjnych klientów REST/SignalR iOS z backendem uruchamia `IOS_DESTINATION_ID=<UDID> ./scripts/test-ios-photo-answer-integration.sh`. Test przeprowadza jednego gracza iOS oraz dwóch graczy demo przez cztery pytania PhotoAnswer do `Completed`; fixture UI nie jest wliczany do tej suite.
