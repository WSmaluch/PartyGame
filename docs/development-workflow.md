# Workflow deweloperski

1. Nie zmieniaj kontraktów REST ani SignalR bez jednoczesnej aktualizacji dokumentacji i przykładów w `contracts/`.
2. Każdy etap funkcjonalny musi zawierać testy proporcjonalne do dodanej logiki.
3. Nie umieszczaj logiki punktacji ani autorytatywnych reguł w React lub Swift.
4. Nie używaj identyfikatora połączenia SignalR jako trwałego identyfikatora gracza. Połączenie jest efemeryczne i zmienia się po reconnect.
5. Po stronie serwera używaj UTC; konwersja na czas lokalny należy do warstwy prezentacji.
6. Publiczną zmianę pokoju wykonuj przez `IRoomService`, aby wersjonowanie, blokada per pokój i automatyczny start pozostały spójne dla REST i SignalR.
7. Testy integracyjne muszą korzystać z katalogu tymczasowego na SQLite i media oraz usuwać go po zakończeniu.
8. Przed zakończeniem zmiany uruchom z katalogu głównego: `dotnet restore`, `dotnet build`, `dotnet test` i `dotnet format --verify-no-changes`.
9. Przy zmianach przekrojowych sprawdź `npm run lint`, `npm run test`, `npm run build` w obu klientach React oraz build projektu iOS — bez modyfikowania klientów, jeśli etap dotyczy wyłącznie backendu.
10. Backend PhotoAnswer można sprawdzić end-to-end poleceniem `./scripts/demo-photo-answer-game.sh`; mieszany plan 2/2/2 poleceniem `./scripts/demo-mixed-three-types-game.sh`; fixture’y są generowane technicznie i nie zawierają danych użytkowników.
11. Restart testuj na dwóch instancjach hosta używających tego samego pliku SQLite i katalogu mediów. Test samej rekonstrukcji obiektu domenowego nie jest dowodem restartu.
12. Testy współbieżności muszą uruchamiać rzeczywiste zadania przez `Task.WhenAll` i po zakończeniu weryfikować bazę, wersję stanu oraz pliki.
13. Pełna regresja iOS używa `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer`; wykonaj osobno build oraz `xcodebuild test` na urządzeniu zwróconym przez `simctl`.
14. Zmiany PhotoAnswer w kliencie muszą osobno raportować testy fixture UI, integrację z prawdziwym backendem i browser E2E; fixture bez backendu nie może być nazwany pełnym E2E.
15. Przy testach zdjęć używaj wygenerowanych fixture’ów bez danych prywatnych. Nie zapisuj tokenów, multipart body ani pełnych URL-i mediów w logach i artefaktach testowych.
16. Realną integrację PhotoAnswer na symulatorze uruchamiaj przez `IOS_DESTINATION_ID=<UDID> ./scripts/test-ios-photo-answer-integration.sh`; skrypt wymaga wolnego portu `5050`.
17. Backend DrawingAnswer sprawdzaj przez `./scripts/demo-drawing-answer-game.sh`; pełny scenariusz korzysta z prawdziwego hosta, SQLite, LocalMediaStorage, multipart i oficjalnego klienta SignalR.
18. DrawingAnswer klientowy w Etapie 5B wymaga osobnego testu modeli, renderera PNG, multipart, draftu oraz tras routingu; publiczny Display nie może importować ani przechowywać `PlayerPrivateGameState`.
19. Testy UI DrawingAnswer mogą używać fixture’ów do ergonomii płótna, ale dopiero scenariusz z prawdziwym hostem, SQLite, LocalMediaStorage, REST multipart i SignalR jest E2E.
20. Wtórny błąd `simctl` w logach testów wynika z niewłaściwych ścieżek; uruchamiaj skrypty z wymuszonym `DEVELOPER_DIR` oraz `PATH` uwzględniającym zarówno Xcode, jak i `.NET` dla integracji iOS E2E.
21. Środowiska CI oraz diagnostyka wymagają wielokrotnych przebiegów testów (tzw. stress runy, np. 10x dla `GameSessionStoreTests`), aby potwierdzić brak flakiness wywołanych nieanulowanymi asynchronicznymi taskami i procesami `tearDown`.
