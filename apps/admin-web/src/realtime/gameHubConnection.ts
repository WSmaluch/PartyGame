import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { apiUrl } from '../api/apiConfig';
import type {
  GameHubStatus,
  GameHubStatusListener,
  HubPingResponse,
} from './types';

class GameHubConnection {
  private readonly connection: HubConnection;
  private readonly listeners = new Set<GameHubStatusListener>();
  private startPromise?: Promise<void>;
  private status: GameHubStatus = 'disconnected';

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/game'))
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.onreconnecting(() => this.setStatus('reconnecting'));
    this.connection.onreconnected(() => this.setStatus('connected'));
    this.connection.onclose((error) =>
      this.setStatus(error ? 'error' : 'disconnected'),
    );
  }

  subscribe(listener: GameHubStatusListener): () => void {
    this.listeners.add(listener);
    listener(this.status);
    return () => this.listeners.delete(listener);
  }

  async start(): Promise<void> {
    if (this.connection.state === HubConnectionState.Connected) return;
    if (this.startPromise) return this.startPromise;
    this.setStatus('connecting');
    this.startPromise = this.connection
      .start()
      .then(() => this.setStatus('connected'))
      .catch((error: unknown) => {
        this.setStatus('error');
        throw error;
      })
      .finally(() => {
        this.startPromise = undefined;
      });
    return this.startPromise;
  }

  async stop(): Promise<void> {
    await this.startPromise?.catch(() => undefined);
    if (this.connection.state !== HubConnectionState.Disconnected)
      await this.connection.stop();
    this.setStatus('disconnected');
  }

  async ping(): Promise<HubPingResponse> {
    if (this.connection.state !== HubConnectionState.Connected) {
      throw new Error('SignalR nie jest połączony.');
    }
    return this.connection.invoke<HubPingResponse>('Ping');
  }

  private setStatus(status: GameHubStatus): void {
    this.status = status;
    this.listeners.forEach((listener) => listener(status));
  }
}

export const gameHubConnection = new GameHubConnection();
