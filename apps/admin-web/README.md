# PartyGame Admin

Panel operatora zbudowany w React, TypeScript i Vite. Zawiera edytor pakietów treści (wersje Draft/Published/Archived, kategorie, pytania, walidacja i publikacja — zob. [Etap 6A](../../docs/stage-06a-admin-content-editor.md)) oraz diagnostykę z support bundle. W Production wymaga tokenu operatora, przechowywanego wyłącznie w pamięci strony ([security hardening](../../docs/security/security-hardening.md)).

```bash
npm install
npm run dev
```

Aplikacja działa pod `http://localhost:5174/admin`. W trybie developerskim adres backendu pochodzi z jawnego `VITE_API_BASE_URL`. Artefakt release ładuje `/config.json`; brak poprawnej konfiguracji pokazuje błąd zamiast niejawnego fallbacku na `localhost`.

Walidacja: `npm run lint`, `npm run test`, `npm run build` oraz opcjonalnie `npm run format:check`.
