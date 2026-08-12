export const translations = {
  pl: {
    title: 'PartyGame', roomCode: 'Kod pokoju', nickname: 'Twój nick', join: 'Dołącz do gry', joining: 'Dołączanie…',
    joined: 'Dołączono do gry', room: 'Pokój', player: 'Gracz', connection: 'Połączenie', waiting: 'Oczekiwanie na pozostałych graczy…',
    connecting: 'Łączenie', connected: 'Połączono', reconnecting: 'Ponowne łączenie', disconnected: 'Rozłączono',
    roomRequired: 'Wpisz kod pokoju.', nicknameRequired: 'Wpisz nick.', roomLength: 'Kod pokoju ma 4 znaki.', nicknameLength: 'Nick musi mieć od 2 do 20 znaków.',
    roomNotFound: 'Nie znaleziono pokoju.', roomStarted: 'Do tego pokoju nie można już dołączyć.', invalid: 'Dane są nieprawidłowe.', network: 'Brak połączenia z serwerem. Spróbuj ponownie.', server: 'Serwer jest chwilowo niedostępny. Spróbuj ponownie.',
    players: 'Gracze', configurationError: 'Nie można uruchomić PartyGame. Sprawdź konfigurację serwera.', lobby: 'Lobby', ready: 'Gotowy', notReady: 'Nie jestem gotowy', waitingStatus: 'Oczekuje', profile: 'Profil', choosePhoto: 'Wybierz zdjęcie', uploadPhoto: 'Zapisz zdjęcie', uploading: 'Wysyłanie zdjęcia…', uploadFailed: 'Nie udało się wysłać zdjęcia. Spróbuj ponownie.', unsupportedPhoto: 'Wybierz plik graficzny.', photoTooLarge: 'Zdjęcie jest zbyt duże.', photoProcessing: 'Nie udało się przygotować zdjęcia.', photoRequired: 'Dodaj zdjęcie, aby oznaczyć gotowość.', sessionExpired: 'Sesja wygasła. Dołącz ponownie do pokoju.', gameStarted: 'Gra wystartowała', gameStartedHint: 'Obsługa pytań w przeglądarce będzie dostępna w kolejnym etapie.', offline: 'Brak połączenia', reconnectingHint: 'Przywracanie połączenia…', connectedStatus: 'Połączono', retry: 'Spróbuj ponownie', avatarOf: 'Avatar gracza', you: 'Ty',
  },
  en: {
    title: 'PartyGame', roomCode: 'Room code', nickname: 'Your nickname', join: 'Join game', joining: 'Joining…',
    joined: 'Joined the game', room: 'Room', player: 'Player', connection: 'Connection', waiting: 'Waiting for other players…',
    connecting: 'Connecting', connected: 'Connected', reconnecting: 'Reconnecting', disconnected: 'Disconnected',
    roomRequired: 'Enter a room code.', nicknameRequired: 'Enter your nickname.', roomLength: 'Room code has 4 characters.', nicknameLength: 'Nickname must have 2 to 20 characters.',
    roomNotFound: 'Room not found.', roomStarted: 'This room can no longer be joined.', invalid: 'The provided details are invalid.', network: 'Cannot reach the server. Please try again.', server: 'The server is temporarily unavailable. Please try again.',
    players: 'Players', configurationError: 'PartyGame could not start. Check the server configuration.', lobby: 'Lobby', ready: 'Ready', notReady: 'Not ready', waitingStatus: 'Waiting', profile: 'Profile', choosePhoto: 'Choose photo', uploadPhoto: 'Save photo', uploading: 'Uploading photo…', uploadFailed: 'Photo upload failed. Please try again.', unsupportedPhoto: 'Choose an image file.', photoTooLarge: 'The photo is too large.', photoProcessing: 'The photo could not be prepared.', photoRequired: 'Add a photo before marking yourself ready.', sessionExpired: 'Your session expired. Join the room again.', gameStarted: 'Game started', gameStartedHint: 'Questions in the browser will be available in a future stage.', offline: 'Offline', reconnectingHint: 'Restoring connection…', connectedStatus: 'Connected', retry: 'Try again', avatarOf: 'Player avatar', you: 'You',
  },
};

export type Locale = keyof typeof translations;
export type TranslationKey = keyof typeof translations.pl;

export function preferredLocale(): Locale {
  return 'pl';
}
