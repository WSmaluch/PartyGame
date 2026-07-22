import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { apiUrl } from '../api/apiConfig';
import type { RoomSnapshot } from '../api/types';
import type { GameHubStatus, GameHubStatusListener, HubPingResponse } from './types';

type SnapshotListener = (snapshot: RoomSnapshot) => void;
type VoidListener = () => void;

class GameHubConnection {
  private readonly connection: HubConnection;
  private readonly statusListeners = new Set<GameHubStatusListener>();
  private readonly snapshotListeners = new Set<SnapshotListener>();
  private readonly startedListeners = new Set<SnapshotListener>();
  private readonly replacedListeners = new Set<VoidListener>();
  private startPromise?: Promise<void>;
  private status: GameHubStatus = 'disconnected';
  private attachedRoomCode?: string;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/game'))
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('RoomSnapshotUpdated', (snapshot: RoomSnapshot) =>
      this.snapshotListeners.forEach((listener) => listener(snapshot)),
    );
    this.connection.on('RoomStarted', (snapshot: RoomSnapshot) =>
      this.startedListeners.forEach((listener) => listener(snapshot)),
    );
    this.connection.on('DisplayReplaced', () =>
      this.replacedListeners.forEach((listener) => listener()),
    );
    this.connection.onreconnecting(() => this.setStatus('reconnecting'));
    this.connection.onreconnected(() => {
      this.setStatus('connected');
      if (this.attachedRoomCode) {
        void this.attachDisplay(this.attachedRoomCode).catch(() => this.setStatus('error'));
      }
    });
    this.connection.onclose((error) => this.setStatus(error ? 'error' : 'disconnected'));
  }

  subscribe(listener: GameHubStatusListener): () => void {
    this.statusListeners.add(listener);
    listener(this.status);
    return () => this.statusListeners.delete(listener);
  }
  onSnapshot(listener: SnapshotListener): () => void {
    this.snapshotListeners.add(listener);
    return () => this.snapshotListeners.delete(listener);
  }
  onRoomStarted(listener: SnapshotListener): () => void {
    this.startedListeners.add(listener);
    return () => this.startedListeners.delete(listener);
  }
  onDisplayReplaced(listener: VoidListener): () => void {
    this.replacedListeners.add(listener);
    return () => this.replacedListeners.delete(listener);
  }

  async start(): Promise<void> {
    if (this.connection.state === HubConnectionState.Connected) return;
    if (this.startPromise) return this.startPromise;
    this.setStatus('connecting');
    this.startPromise = this.connection.start()
      .then(() => this.setStatus('connected'))
      .catch((error: unknown) => { this.setStatus('error'); throw error; })
      .finally(() => { this.startPromise = undefined; });
    return this.startPromise;
  }

  async stop(): Promise<void> {
    await this.startPromise?.catch(() => undefined);
    if (this.connection.state !== HubConnectionState.Disconnected) await this.connection.stop();
    this.setStatus('disconnected');
  }

  async attachDisplay(roomCode: string): Promise<RoomSnapshot> {
    if (this.connection.state !== HubConnectionState.Connected) throw new Error('SignalR nie jest połączony.');
    const snapshot = await this.connection.invoke<RoomSnapshot>('AttachDisplay', roomCode);
    this.attachedRoomCode = roomCode;
    return snapshot;
  }

  forgetAttachment(): void { this.attachedRoomCode = undefined; }

  async ping(): Promise<HubPingResponse> {
    if (this.connection.state !== HubConnectionState.Connected) throw new Error('SignalR nie jest połączony.');
    return this.connection.invoke<HubPingResponse>('Ping');
  }

  private setStatus(status: GameHubStatus): void {
    this.status = status;
    this.statusListeners.forEach((listener) => listener(status));
  }
}

export const gameHubConnection = new GameHubConnection();
