# PartyGame Web Player

Web Player jest przeglądarkowym klientem istniejącego PartyGame. Etap 1A pozwala dołączyć do pokoju, zapisać sesję gracza i połączyć ją z istniejącym SignalR Hub. Nie zawiera jeszcze pełnego przebiegu gry, gotowości, zdjęć profilowych, pytań, głosowania ani wyników.

## Development

```bash
cd apps/player-web
npm ci
npm run dev
npm test
npm run lint
npm run build
```

Domyślny dev server działa pod `http://localhost:5175/play/`. Ustaw `VITE_API_BASE_URL` (oraz opcjonalnie `VITE_SIGNALR_HUB_URL`) w `.env.local`, aby wskazać lokalny lub LAN-owy backend, bez zmieniania kodu źródłowego. Produkcyjna aplikacja pobiera `/play/config.json` i domyślnie działa same-origin.
