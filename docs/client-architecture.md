# Architektura klientów

## Wspólne zasady

Serwer ASP.NET Core jest jedynym źródłem prawdy. Klienci mogą wysyłać intencje użytkownika i prezentować otrzymany stan, ale nie mogą naliczać punktów, wybierać aktywnego pytania ani samodzielnie ustalać stanu rozgrywki.

REST obsługuje operacje żądanie–odpowiedź oraz transfer plików (zdjęcia profilowe, odpowiedzi zdjęciowe i rysunkowe, runda finałowa). SignalR przenosi bieżący stan oraz zdarzenia czasu rzeczywistego. Utrata połączenia nie upoważnia klienta do lokalnego kontynuowania autorytatywnej rozgrywki.

## iOS

Aplikacja SwiftUI jest kontrolerem gracza. Zarządza adresem serwera, prezentuje stan połączenia i wysyła intencje Host/Join oraz odpowiedzi gracza. Warstwa `Networking` izoluje `URLSession` od widoków, a `ServerConfiguration` jest pojedynczym źródłem adresu backendu.

iOS sprawdza serwer przez `GET /health`, a komunikację czasu rzeczywistego realizuje `GameRealtimeClient` oparty na pakiecie `signalr-client-swift`.

## Display

Display jest obowiązkowym ekranem TV. Trasa `/display` pokazuje lobby z kodem QR, etapy pytań, wyniki i ranking na podstawie publicznych snapshotów. Nie przyjmuje roli hosta i nie podejmuje decyzji o stanie gry.

Adres API pochodzi z `VITE_API_BASE_URL` w trybie deweloperskim, a w wydaniu z runtime `config.json`. Klient SignalR utrzymuje jedno połączenie, obsługuje reconnect oraz udostępnia jawne `start`, `stop` i `ping`.

## Admin

Admin działa pod `/admin`. Zawiera edytor pakietów treści (`/admin/content`) i diagnostykę operatora; w Production wymaga tokenu operatora.

Warstwy REST i SignalR mają takie same granice jak w Display, lecz są niezależną implementacją i osobnym pakietem aplikacji.

## Web Player

Web Player działa pod `/play/` i jest przeglądarkowym odpowiednikiem aplikacji iOS: korzysta z tego samego REST API i huba SignalR, nie przechowuje logiki gry i odtwarza sesję gracza po odświeżeniu strony. Szczegóły: [web-player.md](web-player.md).
