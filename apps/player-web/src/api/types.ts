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

export type RoomSnapshot = {
  roomCode: string;
  phase: string;
  stateVersion: number;
  players: PublicPlayer[];
};

export type RoomAccessResponse = {
  roomCode: string;
  playerId: string;
  reconnectToken: string;
  snapshot: RoomSnapshot;
};

export type ResumePlayerResponse = {
  player: PublicPlayer;
  snapshot: RoomSnapshot;
};

export type PlayerSession = {
  roomCode: string;
  playerId: string;
  reconnectToken: string;
  nickname: string;
};
