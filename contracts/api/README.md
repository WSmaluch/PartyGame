# Kontrakty REST API

REST jest używany do tworzenia pokojów, dołączania graczy, pobierania migawek, sprawdzania sesji i przesyłania zdjęć. Kontrakt Etapu 1A znajduje się w [rooms.md](rooms.md), a kompletne payloady w `contracts/examples/`.

Bieżący stan rozgrywki i zdarzenia czasu rzeczywistego będą przesyłane przez SignalR. Serwer jest jedynym źródłem prawdy; klienci wyświetlają stan serwera i przesyłają intencje użytkownika.
