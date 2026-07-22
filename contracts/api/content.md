# Api Treści Content

Dokumentacja endpointu `/api/content/packages` wymaganego przez aplikację mobilną przy tworzeniu pokoju.

## `GET /api/content/packages`
Zwraca listę wszystkich aktywnych pakietów pobranych z bazy danych wraz z kategoriami.

### DTO Odpowiedzi (Array of `PackageResponse`)
```typescript
interface PackageResponse {
  id: string; // Guid
  key: string;
  name: LocalizedText;
  description: LocalizedText;
  categoryCount: number;
  minimumSupportedRounds: number;
  maximumSupportedRounds: number;
  isDefault: boolean;
}

interface LocalizedText {
  pl: string;
  en: string;
}
```

Aplikacja kliencka, wywołując Host Game, musi pobrać z tego API paczki, pozwolić graczowi je zaznaczyć, a wybrane klucze wysłać do:
`POST /api/rooms` pod parametrem `selectedPackageKeys: string[]`.
# Typy pytań

`type` jest tekstowym enumem: `PlayerSelection`, `TextAnswer`, `PhotoAnswer` albo `DrawingAnswer`. Pakiet `starter` zawiera po 50 aktywnych pytań każdego typu (200 łącznie).
