# DrawingAnswer SignalR

`SubmitDrawingAnswerVote(roomCode, playerId, reconnectToken, questionInstanceId, drawingAnswerId)` zapisuje jeden głos uprawnionego gracza. Self-vote jest dozwolony. Ostatni wymagany głos natychmiast przechodzi do wyników. `PlayerPrivateGameStateUpdated` trafia wyłącznie do aktywnego `ConnectionId` gracza i zawiera `hasSubmittedDrawingAnswer`, `ownDrawingAnswerId` i `hasSubmittedDrawingAnswerVote`; Display nie otrzymuje eventu prywatnego.
