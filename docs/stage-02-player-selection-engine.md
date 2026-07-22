# Silnik Głosowania - Etap 2

Dokumentacja aktualnego stanu logiki gry, zbudowanego w oparciu o C# i system powiadomień SignalR.

## Przepływ etapów gry
1. `CategoryIntro` - Pokazuje tylko wylosowaną w paczce `Category`
2. `QuestionIntro` - Pokazuje dodatkowo aktualne wylosowane `Question`.
3. `CollectingPlayerSelections` - Zbieranie odpowiedzi przez SignalR (akceptowane tylko od uprawnionych, 1 raz). Udostępnia listę graczy, którzy już odpowiedzieli bez przecieków.
4. `ShowingQuestionResults` - Wyświetlanie wyników wraz ze sprawdzaniem punktów dodanych w bazie i pełnym rankingiem.
5. `RoundSummary` - Po podliczeniu sumarycznych wszystkich punktów za rundę.
6. `Completed` - Zakończenie rozgrywki.

Wszystkie te stany są ściśle sprawdzane po stronie serwera C# (Entity Framework), który narzuca autorytarną logikę.
Wynik liczy klasa `ScoreCalculator.cs`, która przyznaje 100 punktów graczowi głosującemu pomnożone przez sumaryczną ilość głosów jaka padła na wybraną przez głosującego osobę.
