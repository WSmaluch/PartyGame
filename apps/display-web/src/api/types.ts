export interface HealthResponse {
  status: string;
  service: string;
  version: string;
  utcTime: string;
}

export type RoomPhase = 'Lobby' | 'Started' | 'Completed';

export interface RoomSettings {
  roundCount: number;
  questionsPerRound: number;
  playerSelectionSeconds: number;
  textAnswerSeconds: number;
  votingSeconds: number;
  photoSeconds: number;
  drawingSeconds: number;
  resultPresentationSeconds: number;
  finalRoundEnabled: boolean;
  finalDrawingPasses: number;
}

export interface RoomPlayer {
  id: string;
  nickname: string;
  isHost: boolean;
  isReady: boolean;
  isConnected: boolean;
  hasProfilePhoto: boolean;
  profilePhotoUrl?: string | null;
  score: number;
}

export interface PlayerScoreSnapshot {
  playerId: string;
  score: number;
}

export type LocalizedText = string | { pl?: string; en?: string; defaultText?: string };
export function localizedText(value?: LocalizedText | null): string {
  if (!value) return '';
  if (typeof value === 'string') return value;
  return (navigator.language.toLowerCase().startsWith('pl') ? value.pl : value.en) ?? value.en ?? value.pl ?? value.defaultText ?? '';
}

export type GameStage = 
  | 'NotStarted' 
  | 'CategoryIntro' 
  | 'QuestionIntro' 
  | 'CollectingPlayerSelections' 
  | 'ShowingQuestionResults' 
  | 'RoundSummary' 
  | 'Completed' 
  | 'PausedForDisplay'
  | 'CollectingTextAnswers'
  | 'RevealingTextAnswers'
  | 'CollectingTextAnswerVotes'
  | 'ShowingTextAnswerResults'
  | 'CollectingPhotoAnswers'
  | 'RevealingPhotoAnswers'
  | 'CollectingPhotoAnswerVotes'
  | 'ShowingPhotoAnswerResults'
  | 'CollectingDrawingAnswers'
  | 'RevealingDrawingAnswers'
  | 'CollectingDrawingAnswerVotes'
  | 'ShowingDrawingAnswerResults'
  | (string & {});

export interface GameCategorySnapshot {
  id: string;
  name: LocalizedText;
  description?: LocalizedText;
}

export interface GameQuestionSnapshot {
  id: string;
  text: LocalizedText;
}

export interface ResultVoter {
  playerId: string;
  nickname: string;
  profilePhotoUrl?: string | null;
  pointsAwarded: number;
}

export interface PlayerSelectionResultOption {
  selectedPlayerId: string;
  selectedPlayerNickname: string;
  selectedPlayerPhotoUrl?: string | null;
  voteCount: number;
  isTopResult: boolean;
  voters: ResultVoter[];
}

export interface PlayerSelectionResults {
  questionInstanceId: string;
  answeredPlayers: number;
  requiredPlayers: number;
  missingPlayers: number;
  highestVoteCount: number;
  options: PlayerSelectionResultOption[];
}

export interface TextAnswerOptionVoting {
  answerId: string;
  text: string;
  displayOrder?: number | null;
}

export interface TextAnswerOptionResult {
  answerId: string;
  text: string;
  authorPlayerId: string;
  authorPlayerNickname: string;
  authorPlayerPhotoUrl?: string | null;
  voteCount: number;
  isTopResult: boolean;
  voters: ResultVoter[];
}

export interface TextAnswerResults {
  questionInstanceId: string;
  answeredPlayers: number;
  requiredPlayers: number;
  missingPlayers?: number | null;
  highestVoteCount?: number | null;
  options?: TextAnswerOptionResult[] | null;
  votingOptions?: TextAnswerOptionVoting[] | null;
  submittedAnswerPlayerIds?: string[] | null;
}

export interface AnonymousPhotoAnswer {
  photoAnswerId: string;
  displayPhotoUrl: string;
  thumbnailPhotoUrl: string;
  displayOrder: number;
  width: number;
  height: number;
}

export interface PhotoAnswerResultVoter {
  playerId: string;
  nickname: string;
  profilePhotoUrl?: string | null;
  pointsAwarded: number;
}

export interface PhotoAnswerResultOption {
  photoAnswerId: string;
  displayPhotoUrl: string;
  thumbnailPhotoUrl: string;
  width: number;
  height: number;
  authorPlayerId: string;
  authorNickname: string;
  authorPhotoUrl?: string | null;
  voteCount: number;
  isTopResult: boolean;
  voters: PhotoAnswerResultVoter[];
}

export interface PhotoAnswerResults {
  questionInstanceId: string;
  submittedPlayers: number;
  requiredPlayers: number;
  votedPlayers?: number | null;
  requiredVoters?: number | null;
  missingSubmissionPlayers?: number | null;
  missingVotePlayers?: number | null;
  highestVoteCount?: number | null;
  options?: PhotoAnswerResultOption[] | null;
  anonymousOptions?: AnonymousPhotoAnswer[] | null;
}

export interface AnonymousDrawingOption {
  drawingAnswerId: string;
  displayDrawingUrl?: string | null;
  thumbnailDrawingUrl?: string | null;
  displayOrder?: number | null;
  revealOrder?: number | null;
  width: number;
  height: number;
}

export interface DrawingAnswerResultVoter {
  playerId: string;
  nickname: string;
  profilePhotoUrl?: string | null;
  pointsAwarded: number;
}

export interface DrawingAnswerResultOption {
  drawingAnswerId: string;
  displayDrawingUrl?: string | null;
  thumbnailDrawingUrl?: string | null;
  width: number;
  height: number;
  authorPlayerId: string;
  authorNickname: string;
  authorPhotoUrl?: string | null;
  voteCount: number;
  isTopResult: boolean;
  voters: DrawingAnswerResultVoter[];
}

export interface DrawingAnswerResultsSnapshot {
  questionInstanceId?: string | null;
  submittedPlayers?: number | null;
  requiredPlayers?: number | null;
  submittedDrawingAnswers?: number | null;
  requiredDrawingAnswers?: number | null;
  submittedDrawingAnswerPlayerIds?: string[] | null;
  votedPlayers?: number | null;
  requiredVoters?: number | null;
  highestVoteCount?: number | null;
  anonymousOptions?: AnonymousDrawingOption[] | null;
  options?: DrawingAnswerResultOption[] | null;
}

export interface RankingEntry {
  playerId: string;
  score: number;
  previousScore: number;
  rank: number;
  previousRank: number;
}

export interface RoundSummarySnapshot {
  roundNumber: number;
  ranking?: RankingEntry[] | null;
  rankings?: RankingEntry[] | null;
}

export interface GameSnapshot {
  stage: GameStage;
  currentRoundNumber: number;
  totalRounds: number;
  currentQuestionNumber: number;
  questionsInCurrentRound: number;
  stageEndsAtUtc?: string | null;
  pausedAtUtc?: string | null;
  pausedStage?: string | null;
  pausedRemainingMilliseconds?: number | null;
  scores: PlayerScoreSnapshot[];
  
  currentCategory?: GameCategorySnapshot | null;
  category?: GameCategorySnapshot | null;
  currentQuestion?: GameQuestionSnapshot | null;
  question?: GameQuestionSnapshot | null;
  answeredPlayerIds?: string[] | null;
  answeredPlayers?: number | null;
  requiredPlayers?: number | null;
  playerSelectionResults?: PlayerSelectionResults | null;
  results?: PlayerSelectionResults | null;
  textResults?: TextAnswerResults | null;
  photoAnswerResults?: PhotoAnswerResults | null;
  drawingAnswerResults?: DrawingAnswerResultsSnapshot | null;
  roundSummary?: RoundSummarySnapshot | null;
}

export function gameQuestion(game: GameSnapshot): GameQuestionSnapshot | undefined {
  return game.question ?? game.currentQuestion ?? undefined;
}

export function gameCategory(game: GameSnapshot): GameCategorySnapshot | undefined {
  return game.category ?? game.currentCategory ?? undefined;
}

export interface RoomSnapshot {
  roomCode: string;
  phase: RoomPhase;
  stateVersion: number;
  displayConnected: boolean;
  minimumPlayers: number;
  maximumPlayers: number;
  canStart: boolean;
  settings: RoomSettings;
  players: RoomPlayer[];
  createdAtUtc: string;
  startedAtUtc?: string | null;
  game?: GameSnapshot | null;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
