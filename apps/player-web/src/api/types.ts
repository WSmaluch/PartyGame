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
export type ResultVoter = { playerId: string; nickname: string; profilePhotoUrl?: string | null; pointsAwarded: number };
export type PlayerSelectionResultOption = { selectedPlayerId: string; selectedPlayerNickname: string; selectedPlayerPhotoUrl?: string | null; voteCount: number; isTopResult: boolean; voters: ResultVoter[] };
export type PlayerSelectionResults = { questionInstanceId: string; answeredPlayers: number; requiredPlayers: number; missingPlayers: number; highestVoteCount: number; options: PlayerSelectionResultOption[] };
export type TextAnswerResultOption = { answerId: string; text: string; authorPlayerId: string; authorPlayerNickname: string; authorPlayerPhotoUrl?: string | null; voteCount: number; isTopResult: boolean; voters: ResultVoter[] };
export type TextAnswerResults = {
  questionInstanceId: string;
  answeredPlayers: number;
  requiredPlayers: number;
  missingPlayers?: number | null;
  highestVoteCount?: number | null;
  options?: TextAnswerResultOption[] | null;
  votingOptions?: TextAnswerVotingOption[] | null;
};

export type AnonymousPhotoAnswer = { photoAnswerId: string; displayPhotoUrl?: string | null; thumbnailPhotoUrl?: string | null; displayOrder: number; width: number; height: number };
export type PhotoAnswerResultOption = { photoAnswerId: string; displayPhotoUrl?: string | null; thumbnailPhotoUrl?: string | null; width: number; height: number; authorPlayerId: string; authorNickname: string; authorPhotoUrl?: string | null; voteCount: number; isTopResult: boolean; voters: ResultVoter[] };
export type PhotoAnswerResults = { questionInstanceId: string; submittedPlayers: number; requiredPlayers: number; votedPlayers?: number | null; requiredVoters?: number | null; highestVoteCount?: number | null; options?: PhotoAnswerResultOption[] | null; anonymousOptions?: AnonymousPhotoAnswer[] | null };
export type AnonymousDrawingAnswer = { drawingAnswerId: string; displayDrawingUrl?: string | null; thumbnailDrawingUrl?: string | null; width: number; height: number; revealOrder?: number | null; displayOrder?: number | null };
export type DrawingAnswerResultOption = { drawingAnswerId: string; displayDrawingUrl?: string | null; thumbnailDrawingUrl?: string | null; width: number; height: number; authorPlayerId: string; authorNickname: string; authorPhotoUrl?: string | null; voteCount: number; isTopResult: boolean; voters: ResultVoter[] };
export type DrawingAnswerResults = { questionInstanceId?: string | null; submittedPlayers?: number | null; requiredPlayers?: number | null; votedPlayers?: number | null; requiredVoters?: number | null; highestVoteCount?: number | null; options?: DrawingAnswerResultOption[] | null; anonymousOptions?: AnonymousDrawingAnswer[] | null };
export type RankingEntry = { playerId: string; nickname?: string | null; profilePhotoUrl?: string | null; score: number; rank?: number | null };
export type RoundSummary = { roundNumber: number; ranking?: RankingEntry[] | null; rankings?: RankingEntry[] | null; hasNextRound?: boolean | null; nextRoundNumber?: number | null };

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
  playerSelectionResults?: PlayerSelectionResults | null;
  results?: PlayerSelectionResults | null;
  roundSummary?: RoundSummary | null;
  ranking?: RankingEntry[] | null;
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
