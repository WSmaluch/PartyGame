# PartyGame Admin

Techniczny panel diagnostyczny zbudowany w React, TypeScript i Vite. Moduły zarządzania są wyłącznie bezpiecznymi placeholderami.

```bash
npm install
npm run dev
```

Aplikacja działa pod `http://localhost:5174/admin`. W trybie developerskim adres backendu pochodzi z jawnego `VITE_API_BASE_URL`. Artefakt release ładuje `/config.json`; brak poprawnej konfiguracji pokazuje błąd zamiast niejawnego fallbacku na `localhost`.

Walidacja: `npm run lint`, `npm run test`, `npm run build` oraz opcjonalnie `npm run format:check`.
