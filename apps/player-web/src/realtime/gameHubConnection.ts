import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { signalRHubUrl } from '../api/apiConfig';
import type {
  PlayerPrivateGameState,
  PlayerSession,
  RoomSnapshot,
} from '../api/types';

export type ConnectionStatus =
  'connecting' | 'connected' | 'reconnecting' | 'disconnected';
type StatusListener = (status: ConnectionStatus) => void;
type SnapshotListener = (snapshot: RoomSnapshot) => void;
type GameStartedListener = (snapshot: RoomSnapshot) => void;
type PrivateStateListener = (state: PlayerPrivateGameState) => void;
type HubPingResponse = { utcTime: string };

export class GameHubConnection {
  private connection?: ReturnType<HubConnectionBuilder['build']>;
  private attachment?: PlayerSession;
  private startPromise?: Promise<void>;
  private status: ConnectionStatus = 'disconnected';
  private readonly statusListeners = new Set<StatusListener>();
  private readonly snapshotListeners = new Set<SnapshotListener>();
  private readonly startedListeners = new Set<GameStartedListener>();
  private readonly privateStateListeners = new Set<PrivateStateListener>();
  private serverOffsetMilliseconds = 0;

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

  onPrivateState(listener: PrivateStateListener): () => void {
    this.privateStateListeners.add(listener);
    return () => this.privateStateListeners.delete(listener);
  }

  serverNow(): number {
    return Date.now() + this.serverOffsetMilliseconds;
  }

  async attach(session: PlayerSession): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    this.attachment = session;
    await this.start(connection);
    const snapshot = await connection.invoke<RoomSnapshot>(
      'AttachPlayer',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
    );
    void this.syncClock(connection);
    return snapshot;
  }

  async setReady(
    session: PlayerSession,
    isReady: boolean,
  ): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    return connection.invoke<RoomSnapshot>(
      'SetReady',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
      isReady,
    );
  }

  async leaveLobby(session: PlayerSession): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    const snapshot = await connection.invoke<RoomSnapshot>(
      'LeaveLobby',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
    );
    // A reconnect after an acknowledged leave must never attach the old
    // identity again. The browser persistence is cleared by App at the same
    // transaction boundary.
    this.attachment = undefined;
    return snapshot;
  }

  async playAgain(session: PlayerSession): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    return connection.invoke<RoomSnapshot>(
      'PlayAgain',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
    );
  }

  async getRoomSnapshot(roomCode: string): Promise<RoomSnapshot> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    return connection.invoke<RoomSnapshot>('GetRoomSnapshot', roomCode);
  }

  async submitPlayerSelection(
    session: PlayerSession,
    selectedPlayerId: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    await this.invokeSubmission(
      'SubmitPlayerSelectionWithSubmission',
      session,
      selectedPlayerId,
      questionInstanceId,
      clientSubmissionId,
    );
  }

  async submitTextAnswer(
    session: PlayerSession,
    text: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    await this.invokeSubmission(
      'SubmitTextAnswerWithSubmission',
      session,
      text,
      questionInstanceId,
      clientSubmissionId,
    );
  }

  async submitTextAnswerVote(
    session: PlayerSession,
    selectedAnswerId: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    await this.invokeSubmission(
      'SubmitTextAnswerVoteWithSubmission',
      session,
      selectedAnswerId,
      questionInstanceId,
      clientSubmissionId,
    );
  }

  async submitPhotoAnswerVote(
    session: PlayerSession,
    selectedAnswerId: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    await connection.invoke<void>(
      'SubmitPhotoAnswerVoteWithSubmission',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
      questionInstanceId,
      selectedAnswerId,
      clientSubmissionId,
    );
  }

  async submitDrawingAnswerVote(
    session: PlayerSession,
    selectedAnswerId: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    await connection.invoke<void>(
      'SubmitDrawingAnswerVoteWithSubmission',
      session.roomCode,
      session.playerId,
      session.reconnectToken,
      questionInstanceId,
      selectedAnswerId,
      clientSubmissionId,
    );
  }

  private async invokeSubmission(
    method: string,
    session: PlayerSession,
    value: string,
    questionInstanceId: string,
    clientSubmissionId: string,
  ): Promise<void> {
    const connection = this.ensureConnection();
    if (connection.state !== HubConnectionState.Connected)
      throw new Error('not-connected');
    await connection.invoke<void>(
      method,
      session.roomCode,
      session.playerId,
      session.reconnectToken,
      value,
      questionInstanceId,
      clientSubmissionId,
    );
  }

  private start(
    connection: ReturnType<HubConnectionBuilder['build']>,
  ): Promise<void> {
    if (connection.state === HubConnectionState.Connected)
      return Promise.resolve();
    // While SignalR reconnects on its own, onreconnected re-attaches the
    // player; a second start() would throw and mislabel the status.
    if (connection.state === HubConnectionState.Reconnecting)
      return Promise.reject(new Error('reconnecting'));
    // Session restore, visibility changes and joins may attach concurrently;
    // they share one start instead of racing start() on the same connection.
    this.startPromise ??= (async () => {
      this.setStatus('connecting');
      try {
        await connection.start();
        this.setStatus('connected');
      } catch (error) {
        // A failed start never raises onclose. Report it so the lobby offers
        // its retry action instead of showing "connecting" indefinitely.
        this.setStatus('disconnected');
        throw error;
      } finally {
        this.startPromise = undefined;
      }
    })();
    return this.startPromise;
  }

  private ensureConnection() {
    if (this.connection) return this.connection;
    const connection = new HubConnectionBuilder()
      .withUrl(signalRHubUrl())
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000])
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on('RoomSnapshotUpdated', (snapshot: RoomSnapshot) =>
      this.snapshotListeners.forEach((listener) => listener(snapshot)),
    );
    connection.on('RoomStarted', (snapshot: RoomSnapshot) =>
      this.startedListeners.forEach((listener) => listener(snapshot)),
    );
    connection.on(
      'PlayerPrivateGameStateUpdated',
      (state: PlayerPrivateGameState) =>
        this.privateStateListeners.forEach((listener) => listener(state)),
    );
    connection.onreconnecting(() => this.setStatus('reconnecting'));
    connection.onreconnected(() => {
      this.setStatus('connected');
      if (this.attachment)
        void this.attach(this.attachment).catch(() =>
          this.setStatus('disconnected'),
        );
    });
    connection.onclose(() => this.setStatus('disconnected'));
    this.connection = connection;
    return connection;
  }

  private setStatus(status: ConnectionStatus): void {
    this.status = status;
    this.statusListeners.forEach((listener) => listener(status));
  }

  private async syncClock(
    connection: ReturnType<HubConnectionBuilder['build']>,
  ): Promise<void> {
    try {
      const receivedAt = Date.now();
      const response = await connection.invoke<HubPingResponse>('Ping');
      const serverTime = Date.parse(response.utcTime);
      if (!Number.isNaN(serverTime))
        this.serverOffsetMilliseconds = serverTime - receivedAt;
    } catch {
      /* A countdown remains presentational when clock sync is unavailable. */
    }
  }
}

export const gameHubConnection = new GameHubConnection();
