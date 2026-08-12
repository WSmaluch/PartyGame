import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { signalRHubUrl } from '../api/apiConfig';
import type { PlayerSession, RoomSnapshot } from '../api/types';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
type StatusListener = (status: ConnectionStatus) => void;
type SnapshotListener = (snapshot: RoomSnapshot) => void;
type GameStartedListener = (snapshot: RoomSnapshot) => void;

export class GameHubConnection {
  private connection?: ReturnType<HubConnectionBuilder['build']>;
  private attachment?: PlayerSession;
  private status: ConnectionStatus = 'disconnected';
  private readonly statusListeners = new Set<StatusListener>();
  private readonly snapshotListeners = new Set<SnapshotListener>();
  private readonly startedListeners = new Set<GameStartedListener>();

  subscribe(listener: StatusListener): () => void {
    this.statusListeners.add(listener);
    listener(this.status);
    return () => this.statusListeners.delete(listener);
  }

  onSnapshot(listener: SnapshotListener): () => void {
    this.snapshotListeners.add(listener);
    return () => this.snapshotListeners.delete(listener);
  }

  onGameStarted(listener: GameStartedListener): () => void {
    this.startedListeners.add(listener);
    return () => this.startedListeners.delete(listener);
  }

  async attach(session: PlayerSession): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    this.attachment = session;
    if (connection.state !== HubConnectionState.Connected) {
      this.setStatus('connecting');
      await connection.start();
      this.setStatus('connected');
    }
    return connection.invoke<RoomSnapshot>('AttachPlayer', session.roomCode, session.playerId, session.reconnectToken);
  }

  async setReady(session: PlayerSession, isReady: boolean): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected) throw new Error('not-connected');
    return connection.invoke<RoomSnapshot>('SetReady', session.roomCode, session.playerId, session.reconnectToken, isReady);
  }

  private ensureConnection() {
    if (this.connection) return this.connection;
    const connection = new HubConnectionBuilder().withUrl(signalRHubUrl())
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000]).configureLogging(LogLevel.Warning).build();
    connection.on('RoomSnapshotUpdated', (snapshot: RoomSnapshot) => this.snapshotListeners.forEach((listener) => listener(snapshot)));
    connection.on('RoomStarted', (snapshot: RoomSnapshot) => this.startedListeners.forEach((listener) => listener(snapshot)));
    connection.onreconnecting(() => this.setStatus('reconnecting'));
    connection.onreconnected(() => {
      this.setStatus('connected');
      if (this.attachment) void this.attach(this.attachment).catch(() => this.setStatus('disconnected'));
    });
    connection.onclose(() => this.setStatus('disconnected'));
    this.connection = connection;
    return connection;
  }

  private setStatus(status: ConnectionStatus): void {
    this.status = status;
    this.statusListeners.forEach((listener) => listener(status));
  }
}

export const gameHubConnection = new GameHubConnection();
