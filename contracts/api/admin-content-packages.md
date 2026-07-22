# Pakiety treści Admin API

`GamePackage.Id` jest identyfikatorem konkretnej wersji. `logicalPackageId` łączy wersje jednej rodziny, a `version` rośnie od 1. Status ma cykl `Draft → Published → Archived`; edytowalny jest wyłącznie `Draft`.

## Endpointy

- `GET /api/admin/content-packages` — lista wersji, daty publikacji/archiwizacji, liczniki kategorii i pytań oraz rozkład typów.
- `GET /api/admin/content-packages/{versionId}` — metadane konkretnej wersji, kategorie i aktualny `concurrencyToken`.
- `POST /api/admin/content-packages` — tworzy Draft v1 nowej rodziny.
- `PATCH /api/admin/content-packages/{versionId}` — aktualizuje metadane Draftu; wymaga świeżego tokenu.
- `POST /api/admin/content-packages/{versionId}/create-draft` — kopiuje Published lub Archived do nowego Draftu następnej wersji.

`create-draft` jest głęboką kopią: package, categories i questions mają nowe identyfikatory i tokeny, przy zachowaniu kluczy, treści, aktywności i kolejności. SQLite ma częściowy unikalny indeks dla `(LogicalPackageId, Status=Draft)`, więc nawet równoległe procesy nie zapiszą dwóch Draftów. Konflikt zwraca `409 content_package_already_has_draft`.

## Przykłady

```json
{ "id": "version-id", "logicalPackageId": "family-id", "version": 2, "status": "Draft", "concurrencyToken": "fresh-token" }
```

```json
{ "code": "content_concurrency_conflict", "message": "Pakiet został zmieniony w innej sesji." }
```
