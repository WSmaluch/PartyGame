export type PublicPlayer = {
  id: string;
  nickname: string;
  isHost: boolean;
  isReady: boolean;
  isConnected: boolean;
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

export type PlayerSession = {
  roomCode: string;
  playerId: string;
  reconnectToken: string;
  nickname: string;
};
