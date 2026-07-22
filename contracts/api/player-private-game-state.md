# PlayerPrivateGameState

Prywatny stan jest zwracany w create/join/resume/upload i emitowany jako `PlayerPrivateGameStateUpdated` wyłącznie do aktywnego połączenia gracza. Dla PhotoAnswer zawiera `hasSubmittedPhotoAnswer`, `ownPhotoAnswerId` i `hasSubmittedPhotoAnswerVote`; dla DrawingAnswer analogiczne `hasSubmittedDrawingAnswer`, `ownDrawingAnswerId` i `hasSubmittedDrawingAnswerVote`. Display nie otrzymuje tego eventu ani tych pól.
