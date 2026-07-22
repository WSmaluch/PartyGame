# Klient gry iOS i React: Etap 2B

Wdrożenie kompletnego rozwiązania dla trybu gry "PlayerSelection" dla klienta iOS (aplikacja Host) oraz klienta React (TV Display).

## Logika Zegarów (Time Synchronization)
Z powodu latencji sieci, klient nie używa natywnych zegarów do odmierzania stanu pokoju, lecz bazuje na `ServerOffset = ServerTimeUtc - LocalNow`. Zegar odliczający od `StageEndsAtUtc` rysuje wartość względem obliczonej poprawki. Obliczenia przeprowadzane są na początku każdej odpowiedzi z API.
Timer wyświetlany w React korzysta z `requestAnimationFrame` żeby uniknąć obciążającego przerenderowania, a w iOS używa `TimelineView`.

## Cykl Rozgrywki (Stany klienta)
Obaj klienci implementują ścisłe podążanie za serwerowym stanem z flagą zapobiegania "Rollbackom" na podstawie `StateVersion`.

* `Lobby` -> UI Z Etapu 1 (Kody, avatary).
* `CategoryIntro` -> Pierwszy stan dla gry, pokazujący tematykę.
* `QuestionIntro` -> Przedstawienie pytania z bazy treści.
* `CollectingPlayerSelections` -> Klient iOS ukazuje możliwość wysłania głosu (`SubmitPlayerSelection` z przypisanym Id Pytania). React jedynie podsumowuje ilość odpowiedzi.
* `ShowingQuestionResults` -> Po zebraniu głosów serwer przechodzi w `ShowingQuestionResults`, dostarczając wraz z powiadomieniem pełny zestaw `PlayerSelectionResults`. React zaczyna płynną animację odsłaniania rankingów, chyba że czas został drastycznie zredukowany (albo użyto `prefers-reduced-motion`).
* `RoundSummary` -> Wyświetlenie wyników punktowych z danej rundy po całej serii pytań, wraz ze stanem ogólnym.
* `Completed` -> Podsumowanie turnieju z ogłoszeniem zwycięzców.

W przypadku zagubienia wartości Enum dla fazy gry, system nie wysypuje aplikacji, lecz loguje stan jako `unknown` ze wstrzymanym UI (iOS Codable decodes as `case unknown(String)`).

## Reconnect / Resiliency
* Jeśli gracz straci połączenie z SignalR w iOS i powróci z nową otwartą sesją, aplikacja pobiera na nowo obiekt pokoju (ze zaktualizowaną wartością `AnsweredPlayerIds`). Jeśli ID powracającego gracza tam widnieje, ukazuje się stosowny Loader z informacją, w przeciwnym razie okno głosowania zostaje przywrócone w połowie swojego życia na podstawie `StageEndsAtUtc`.
* Zerwanie i odzyskanie połączenia przez Display (TV) natychmiast wyrzuca stan pokoju do `PausedForDisplay`. Powrót telewizora wysyła automatyczny `AttachDisplay`, uwalniając graczy z trybu zamrożenia.
