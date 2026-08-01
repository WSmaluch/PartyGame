# PartyGame Display

Ekran TV zbudowany w React, TypeScript i Vite. Pokazuje diagnostykę REST oraz SignalR bez implementowania logiki gry.

```bash
npm install
npm run dev
```

Aplikacja działa pod `http://localhost:5173/display`. W trybie developerskim adres backendu podaje jawne `VITE_API_BASE_URL`. Artefakt release ładuje `/config.json`; brak poprawnej konfiguracji pokazuje błąd zamiast niejawnego fallbacku na `localhost`.

Walidacja: `npm run lint`, `npm run test`, `npm run build` oraz opcjonalnie `npm run format:check`.
