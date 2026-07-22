export const translations = {
  pl: {
    category: "Kategoria",
    question: "Pytanie",
    waitingForVotes: "Czekamy na głosy...",
    votes: "Głosy",
    points: "Punkty",
    roundSummary: "Podsumowanie rundy",
    gameCompleted: "Koniec gry!",
    paused: "Gra zapauzowana",
    rank: "Miejsce",
    score: "Wynik",
    answered: "Odpowiedziało",
    voted: "Zagłosowało",
    players: "graczy",
    outOf: "z",
    waitingForAnswers: "Czekamy na odpowiedzi...",
  },
  en: {
    category: "Category",
    question: "Question",
    waitingForVotes: "Waiting for votes...",
    votes: "Votes",
    points: "Points",
    roundSummary: "Round Summary",
    gameCompleted: "Game Completed!",
    paused: "Game Paused",
    rank: "Rank",
    score: "Score",
    answered: "Answered",
    voted: "Voted",
    players: "players",
    outOf: "out of",
    waitingForAnswers: "Waiting for answers...",
  }
};

export type Language = keyof typeof translations;
export const currentLang: Language = 'pl';
export function t(key: keyof typeof translations['pl']) {
  return translations[currentLang][key];
}
