# Upload odpowiedzi zdjęciowej

`POST /api/rooms/{roomCode}/questions/{questionInstanceId}/photo-answers` przyjmuje `multipart/form-data`: `playerId`, `reconnectToken`, `clientSubmissionId` (UUID) i `photo`. Akceptowane są rzeczywiście dekodowalne JPEG/PNG do 10 MB i 6000×6000, minimum 320×320.

Retry tego samego `clientSubmissionId` przez tego samego gracza i pytanie zwraca ten sam `photoAnswerId` bez nowego pliku, rekordu i `stateVersion`. Inne ID po odpowiedzi daje `photo_answer_already_submitted`. Odpowiedź zawiera `photoAnswerId`, prywatny stan gracza i publiczny snapshot; nigdy storage key ani ścieżkę.

Kontrolowane kody błędów są zwracane w `ProblemDetails.extensions.code`.
