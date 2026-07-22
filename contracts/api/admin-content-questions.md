# Admin Content Questions API

`GET /api/admin/content-packages/{packageVersionId}/questions` zwraca paginowaną listę pytań wraz z aktualnym `packageConcurrencyToken`.

Obsługiwane parametry: `search`, `categoryId`, `questionType`, `isEnabled`, `missingTranslation`, `validationErrors`, `page`, `pageSize` (1–100) i `sort`. Dozwolone sortowania to `sortOrderAsc`, `sortOrderDesc`, `updatedDesc`, `updatedAsc`, `keyAsc`, `keyDesc` oraz `typeAsc`; każde ma tie-breaker `Id ASC`. Obca kategoria daje 400 `content_category_not_found`, a nieznana kolejność 400 `content_invalid_sort`.

Przykładowa odpowiedź:

```json
{
  "items": [{ "id": "…", "packageId": "…", "categoryId": "…", "categoryKey": "fun", "categoryNamePl": "Zabawne", "key": "fun_1", "questionType": "TextAnswer", "textPl": "…", "textEn": "…", "minimumPlayers": 3, "sortOrder": 0, "isEnabled": true, "createdAtUtc": "…", "updatedAtUtc": "…", "concurrencyToken": "…", "validationErrors": [] }],
  "page": 1, "pageSize": 25, "totalItems": 1, "totalPages": 1, "packageConcurrencyToken": "…"
}
```

`PATCH`, `DELETE`, `POST …/duplicate` i `POST …/reorder` akceptują aktualne tokeny pytania i/lub pakietu. Konflikt jest zwracany jako HTTP 409 `content_concurrency_conflict`. Mutacje zwracają nowy token pakietu; reorder zwraca pełną, zapisaną kolejność kategorii. Reorder przyjmuje wyłącznie kompletną listę pytań jednej kategorii, bez duplikatów identyfikatorów i pozycji oraz bez wartości ujemnych.

Pytania można zmieniać tylko w `Draft`; listowanie i filtry pozostają dostępne w `Published` oraz `Archived`.

`GET /api/admin/content-packages/{packageVersionId}/questions/{questionId}` zwraca szczegóły pytania, `packageConcurrencyToken` i `packageStatus`; pytanie z innego pakietu jest traktowane jako 404. `POST` tworzy pytanie, a `PATCH` przyjmuje `categoryId`, `key`, `type`, teksty, `minimumPlayers`, `sortOrder`, `isActive`, `concurrencyToken` i `packageConcurrencyToken`.

Klucz ma postać `lowercase_snake_case`. Treści są plain textem, maksymalnie 500 znaków. Pytanie `PlayerSelection` akceptuje wyłącznie `{player}` lub `{player:n}`; niedomknięte, puste i nieznane placeholdery zwracają `content_invalid_placeholder`. Błędy walidacji są zwracane jako `content_validation_failed`, a konflikt tokenów jako HTTP 409 `content_concurrency_conflict`.
