# PartyGame Display

Ekran TV zbudowany w React, TypeScript i Vite. Pokazuje diagnostykę REST oraz SignalR bez implementowania logiki gry.

```bash
npm install
npm run dev
```

Aplikacja działa pod `http://localhost:5173/display`. Adres backendu jest konfigurowany wyłącznie przez `VITE_API_BASE_URL`; wartości startowe zawierają `.env.development` i `.env.example`.

Walidacja: `npm run lint`, `npm run test`, `npm run build` oraz opcjonalnie `npm run format:check`.
