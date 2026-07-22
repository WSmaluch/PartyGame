# Media DrawingAnswer

Warianty są dostępne wyłącznie przez `GET /api/media/{mediaAssetId}/{variant}`, gdzie `variant` to `display` albo `thumbnail`. Oba zwracają `image/png`. Brak assetu, wariantu lub pliku zwraca 404. Fizyczny root i klucze storage pozostają wewnętrzne.
