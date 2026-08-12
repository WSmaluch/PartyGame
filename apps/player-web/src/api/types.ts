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

export type AnonymousPhotoAnswer = { photoAnswerId: string; displayPhotoUrl?: string | null; thumbnailPhotoUrl?: string | null; displayOrder: number; width: number; height: number };
export type PhotoAnswerResults = { questionInstanceId: string; submittedPlayers: number; requiredPlayers: number; anonymousOptions?: AnonymousPhotoAnswer[] | null };
export type AnonymousDrawingAnswer = { drawingAnswerId: string; displayDrawingUrl?: string | null; thumbnailDrawingUrl?: string | null; width: number; height: number; revealOrder?: number | null; displayOrder?: number | null };
export type DrawingAnswerResults = { questionInstanceId?: string | null; submittedPlayers?: number | null; requiredPlayers?: number | null; anonymousOptions?: AnonymousDrawingAnswer[] | null };

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
  photoAnswerResults?: PhotoAnswerResults | null;
  drawingAnswerResults?: DrawingAnswerResults | null;
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
  hasSubmittedPhotoAnswer?: boolean;
  ownPhotoAnswerId?: string | null;
  hasSubmittedPhotoAnswerVote?: boolean;
  hasSubmittedDrawingAnswer?: boolean;
  ownDrawingAnswerId?: string | null;
  hasSubmittedDrawingAnswerVote?: boolean;
  isEligibleForDrawingAnswer?: boolean;
};

export type MediaUploadResponse = { playerPrivateGameState: PlayerPrivateGameState; roomSnapshot: RoomSnapshot };

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
