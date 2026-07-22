# Kontrakt GameSnapshot

Ten dokument dokumentuje kontrakt GameSnapshot, przesyłany z backendu w ramach odpowiedzi na wywołanie z `/api/rooms/{code}` lub uaktualnień z SignalR.

## Struktura `GameSnapshot`
- `Stage` (string): Aktualny etap (np. "CategoryIntro", "ShowingQuestionResults")
- `CurrentRoundNumber` (int)
- `TotalRounds` (int)
- `CurrentQuestionNumber` (int)
- `QuestionsInCurrentRound` (int)
- `StageEndsAtUtc` (DateTimeOffset?)
- `PausedAtUtc` (DateTimeOffset?)
- `PausedStage` (string?)
- `PausedRemainingMilliseconds` (double?)
- `Scores` (Array of PlayerScoreSnapshot)
- `CompletedAtUtc` (DateTimeOffset?)
- `TotalPlayedQuestions` (int?)

### Sekcje opcjonalne zależnie od Etapu:
- `Category` (GameCategorySnapshot?)
- `Question` (GameQuestionSnapshot?)
- `Results` (PlayerSelectionResults?)
- `RoundSummary` (RoundSummarySnapshot?)
- `Ranking` (Array of RankingEntry?)
- `AnsweredPlayerIds` (Array of Guid?)
- `AnsweredPlayers` (int?)
- `RequiredPlayers` (int?)

**Polityka Prywatności:**
W czasie trwania `CollectingPlayerSelections`, model **nie zawiera** obiektu `Results`, chroniąc informacje przed wyciekiem (co zostało potwierdzone nowymi testami bezpieczeństwa).
# PhotoAnswer

Pole `photoAnswerResults` jest zależne od etapu: podczas zbierania zawiera tylko liczniki; podczas reveal/głosowania `anonymousOptions`; podczas wyników `options` z autorami, voterami i punktami. `MediaAssetId` oraz storage keys nigdy nie są publiczne.
