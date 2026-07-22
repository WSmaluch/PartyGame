export type GameHubStatus =
  'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'error';

export interface HubPingResponse {
  status: string;
  utcTime: string;
}

export type GameHubStatusListener = (status: GameHubStatus) => void;
