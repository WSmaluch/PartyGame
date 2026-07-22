# Admin Content Categories API

Endpointy są dostępne pod `/api/admin/content-packages/{packageVersionId}/categories`. Admin API nie ma jeszcze produkcyjnego uwierzytelniania i może być używane wyłącznie w zaufanym środowisku lokalnym.

## DTO

Kategoria zawiera `id`, `packageId`, `key`, `namePl`, `nameEn`, `descriptionPl`, `descriptionEn`, `isActive`, `sortOrder`, `questionCount` i `concurrencyToken`. Lista oraz każda skuteczna mutacja zwraca także `packageConcurrencyToken`.

## Operacje

- `GET /categories` zwraca `{ items, packageConcurrencyToken }`, stabilnie posortowane po `sortOrder`, następnie `id`.
- `POST /categories` wymaga metadanych kategorii oraz aktualnego `packageConcurrencyToken`.
- `PATCH /categories/{categoryId}` wymaga tokenu kategorii i pakietu. Pozwala zmieniać klucz, tłumaczenia, opisy, aktywność i kolejność.
- `DELETE /categories/{categoryId}` wymaga obu tokenów. Domyślny `mode=reject` nie usuwa kategorii zawierającej pytania. `mode=deleteQuestions` jawnie usuwa pytania i kategorię. `mode=moveQuestions&targetCategoryId=...` przenosi pytania do kategorii tej samej wersji i normalizuje ich kolejność.
- `POST /categories/reorder` wymaga aktualnego tokenu pakietu i pełnej listy unikalnych ID oraz pozycji.

Nieaktualny lub brakujący token zwraca HTTP 409 z kodem `content_concurrency_conflict`. `Published` i `Archived` są tylko do odczytu; wszystkie mutacje są odrzucane kodem `content_package_not_editable`.

## Concurrency guarantees and race behavior

Wszystkie mutacje kategorii używają tokenu kategorii oraz wersji pakietu. `update` kontra `update`, `delete` kontra `update` i `reorder` kontra `update` mają jednego logicznego zwycięzcę; druga operacja otrzymuje HTTP 409, a `DbUpdateConcurrencyException` jest mapowany na ten sam kod. `moveQuestions` i `deleteQuestions` są pojedynczym zapisem EF Core, więc pytania są w całości w źródle, w całości w celu albo pozostają w istniejącej kategorii, gdy aktualizacja pytania wygra konflikt. API nie zwraca częściowo zapisanego reorderu ani osieroconych pytań.

## Przykład PATCH

```json
{
  "namePl": "Zabawne",
  "isActive": false,
  "concurrencyToken": "category-token",
  "packageConcurrencyToken": "package-token"
}
```
