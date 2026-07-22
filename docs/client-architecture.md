# Architektura klientów

## Wspólne zasady

Serwer ASP.NET Core jest jedynym źródłem prawdy. Klienci mogą wysyłać intencje użytkownika i prezentować otrzymany stan, ale nie mogą naliczać punktów, wybierać aktywnego pytania ani samodzielnie ustalać stanu rozgrywki.

REST obsługuje operacje żądanie–odpowiedź i w przyszłości transfer plików. SignalR przenosi bieżący stan oraz zdarzenia czasu rzeczywistego. Utrata połączenia nie upoważnia klienta do lokalnego kontynuowania autorytatywnej rozgrywki.

## iOS

Aplikacja SwiftUI jest kontrolerem gracza. Zarządza adresem serwera, prezentuje stan połączenia i w przyszłości wyśle intencje Host/Join oraz odpowiedzi gracza. Warstwa `Networking` izoluje `URLSession` od widoków, a `ServerConfiguration` jest pojedynczym źródłem adresu backendu.

Na Etapie 0B iOS korzysta obowiązkowo z `GET /health`. `GameRealtimeClient` pozostaje interfejsem do kolejnego etapu, ponieważ integracja SignalR wymaga świadomego wyboru stabilnego pakietu Swift Package Manager.

## Display

Display jest obowiązkowym ekranem TV. Trasa `/display` prezentuje duży stan REST/SignalR, wersję, czas UTC i wynik rzeczywistej metody `Ping`. Nie przyjmuje roli hosta i nie podejmuje decyzji o stanie gry.

Adres API pochodzi wyłącznie z `VITE_API_BASE_URL`. Klient SignalR utrzymuje jedno połączenie, obsługuje reconnect oraz udostępnia jawne `start`, `stop` i `ping`.

## Admin

Admin działa pod `/admin`. Obecnie jest panelem diagnostycznym z placeholderami przyszłych modułów treści i historii. Nie zawiera formularzy ani operacji domenowych.

Warstwy REST i SignalR mają takie same granice jak w Display, lecz są niezależną implementacją i osobnym pakietem aplikacji.
