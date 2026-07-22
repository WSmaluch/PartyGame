# Publikacja i wersjonowanie pakietów

`POST /api/admin/content-packages/{versionId}/publish` przyjmuje `{ "concurrencyToken": "…" }`. Tylko poprawny Draft może przejść do Published; walidacja zwraca `400 content_package_validation_failed` i listę `{ path, code, message }`. Zapis zmienia status, `publishedAtUtc` oraz token w jednej operacji EF. Równoległe publish/update/reorder kończy się jednym zwycięzcą i kontrolowanym `409 content_concurrency_conflict` albo `400 content_package_already_published`/`content_package_not_editable`.

`POST /api/admin/content-packages/{versionId}/archive` wymaga świeżego tokenu i Published. Ustawia `ArchivedAtUtc`; Draft nie jest archiwizowalny. Archiwizacja współdzieli blokadę konkretnej wersji z tworzeniem pokoju: albo pokój zostanie zapisany z tym `contentPackageVersionId` przed archiwizacją, albo request pokoju jest odrzucony. Istniejące pokoje nigdy nie są przepinane.

```json
{ "concurrencyToken": "a5d8…" }
```

```json
{ "code": "content_package_already_has_draft", "message": "Dla tej rodziny pakietów istnieje już aktywna wersja robocza (Draft)." }
```

```json
{ "code": "content_package_not_archivable", "message": "Tylko opublikowana wersja pakietu może zostać zarchiwizowana." }
```
