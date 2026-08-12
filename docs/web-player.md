# PartyGame Web Player (1A)

Web Player to przeglądarkowy klient tego samego PartyGame API i SignalR Hub, co aplikacja iOS. Po wdrożeniu wejście to `http://HOST:PORT/play/`; konfiguracja runtime jest dostępna pod `/play/config.json`.

W etapie 1A użytkownik wpisuje czteroznakowy kod pokoju oraz nick (2–20 znaków). Formularz korzysta z istniejącego `POST /api/rooms/{roomCode}/players`, a następnie łączy się z `/hubs/game` i wywołuje `AttachPlayer`. Parametr `?room=AB12` uzupełnia kod pokoju. Dane sesji gracza (`roomCode`, `playerId`, `reconnectToken`, `nickname`) są zapisywane lokalnie, aby po odświeżeniu strona mogła ponownie wykonać `AttachPlayer`.

Ekran po join pokazuje pokój, gracza, stan połączenia i — gdy snapshot jest dostępny — podstawową listę graczy. Nie jest to jeszcze pełna gra w przeglądarce: Ready, profilowe zdjęcia, pytania, odpowiedzi, głosowanie, media, wyniki i ranking będą kolejnymi etapami.

Lokalny development:

```bash
cd apps/player-web
npm ci
npm run dev
npm test
npm run lint
npm run build
```

Skopiuj `.env.example` do `.env.local`, aby wskazać lokalny backend. Dla release nie edytuj źródeł: `/play/config.json` jest tworzony przez deployment i używa same-origin URL-i.
