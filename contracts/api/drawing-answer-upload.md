# Upload DrawingAnswer

`POST /api/rooms/{roomCode}/questions/{questionInstanceId}/drawing-answers`, `multipart/form-data`:

- `playerId`: UUID;
- `reconnectToken`: token sesji;
- `clientSubmissionId`: UUID idempotencji dla bieżącego pytania;
- `drawing`: finalny `image/png`.

Sukces zwraca `drawingAnswerId`, prywatny stan wywołującego i publiczny snapshot. Retry tego samego ID zwraca istniejący wynik. Kontrakt nigdy nie ujawnia `StorageKey`, `MediaAssetId` ani ścieżki. Błędy mają pole `code`, m.in. `drawing_answer_invalid_image`, `drawing_answer_blank`, `drawing_answer_already_submitted` i `drawing_answer_storage_failed`.
