# Media zdjęciowe

`GET /api/media/{mediaAssetId}/{variant}`, gdzie `variant` to `display` albo `thumbnail`, zwraca JPEG. Endpoint rozwiązuje wyłącznie rekord `MediaAsset` i wewnętrzny storage key; katalog storage nie jest static folderem. Brak rekordu, wariantu lub pliku daje 404.
