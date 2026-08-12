export type PublicPlayer = {
  id: string;
  nickname: string;
  isHost: boolean;
  isReady: boolean;
  isConnected: boolean;
  hasProfilePhoto: boolean;
  profilePhotoUrl?: string | null;
  score: number;
};

export type LocalizedText = string | { pl?: string; en?: string; defaultText?: string };

export function localizedText(value: LocalizedText | undefined | null, locale: 'pl' | 'en'): string {
  if (!value) return '';
  if (typeof value === 'string') return value;
  return value[locale] ?? value.en ?? value.pl ?? value.defaultText ?? '';
}

export type GameQuestion = {
  id: string;
  instanceId?: string | null;
  text: LocalizedText;
};

export type TextAnswerVotingOption = { answerId: string; text: string; displayOrder?: number | null };
export type TextAnswerResults = {
  questionInstanceId: string;
  answeredPlayers: number;
  requiredPlayers: number;
  votingOptions?: TextAnswerVotingOption[] | null;
};

export type GameSnapshot = {
  stage: string;
  currentRoundNumber: number;
  totalRounds: number;
  currentQuestionNumber: number;
  questionsInCurrentRound: number;
  stageEndsAtUtc?: string | null;
  question?: GameQuestion | null;
  currentQuestion?: GameQuestion | null;
  answeredPlayerIds?: string[] | null;
  answeredPlayers?: number | null;
  requiredPlayers?: number | null;
  textResults?: TextAnswerResults | null;
};

export function gameQuestion(game: GameSnapshot): GameQuestion | undefined {
  return game.question ?? game.currentQuestion ?? undefined;
}

export type PlayerPrivateGameState = {
  playerId: string;
  questionInstanceId?: string | null;
  hasSubmittedTextAnswer: boolean;
  ownTextAnswerId?: string | null;
  hasSubmittedTextAnswerVote: boolean;
  isEligibleForTextAnswerVote: boolean;
};

export type RoomSnapshot = {
  roomCode: string;
  phase: string;
  stateVersion: number;
  players: PublicPlayer[];
  game?: GameSnapshot | null;
};

export type RoomAccessResponse = {
  roomCode: string;
  playerId: string;
  reconnectToken: string;
  snapshot: RoomSnapshot;
  privateState: PlayerPrivateGameState;
};

export type ResumePlayerResponse = {
  player: PublicPlayer;
  snapshot: RoomSnapshot;
  privateState: PlayerPrivateGameState;
};

export type PlayerSession = {
  roomCode: string;
  playerId: string;
  reconnectToken: string;
  nickname: string;
};
