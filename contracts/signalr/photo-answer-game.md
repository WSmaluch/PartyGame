# SignalR — PhotoAnswer

Etapy: `QuestionIntro → CollectingPhotoAnswers → RevealingPhotoAnswers → CollectingPhotoAnswerVotes → ShowingPhotoAnswerResults`.

Głos: `SubmitPhotoAnswerVote(roomCode, playerId, reconnectToken, questionInstanceId, photoAnswerId)`. Własne zdjęcie jest prawidłowym wyborem. Kliknięcie jest ostateczne. `PlayerPrivateGameStateUpdated` pozostaje jedynym prywatnym eventem i trafia wyłącznie do aktywnego połączenia gracza.

Podczas zbierania publiczny snapshot pokazuje tylko liczby i ID graczy, którzy odpowiedzieli. Reveal i głosowanie zawierają anonimowe media. Autorzy, głosy i punkty pojawiają się dopiero w wynikach.
