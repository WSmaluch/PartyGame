import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent, FormEvent } from 'react';
import {
  joinRoom,
  PlayerApiError,
  resumePlayer,
  uploadProfilePhoto,
} from './api/playerApi';
import {
  gameQuestion,
  type PlayerSession,
  type PublicPlayer,
  type RoomSnapshot,
} from './api/types';
import { privateActionType } from './game/privateAction';
import { prepareProfilePhoto, ProfilePhotoError } from './media/profilePhoto';
import {
  gameHubConnection,
  type ConnectionStatus,
} from './realtime/gameHubConnection';
import {
  clearPlayerSession,
  loadPlayerSession,
  savePlayerSession,
} from './session/playerSession';
import {
  preferredLocale,
  translations,
  type Locale,
  type TranslationKey,
} from './translations';
import { GameRouter } from './game/GameRouter';
import { HostSetup } from './HostSetup';

type Screen =
  | { kind: 'join' }
  | { kind: 'lobby'; session: PlayerSession; snapshot?: RoomSnapshot }
  | { kind: 'game'; session: PlayerSession; snapshot?: RoomSnapshot };

export default function App() {
  const [locale] = useState<Locale>(preferredLocale);
  const t = (key: TranslationKey) => translations[locale][key];
  const initialRoom = useMemo(
    () =>
      normalizeRoomCode(
        new URLSearchParams(window.location.search).get('room') ?? '',
      ),
    [],
  );
  const initialSession = useMemo(() => loadPlayerSession(), []);
  const [roomCode, setRoomCode] = useState(
    initialSession?.roomCode ?? initialRoom,
  );
  const [nickname, setNickname] = useState(initialSession?.nickname ?? '');
  const [screen, setScreen] = useState<Screen>(() =>
    initialSession
      ? { kind: 'lobby', session: initialSession }
      : { kind: 'join' },
  );
  const [entryMode, setEntryMode] = useState<'join' | 'host'>('join');
  const [connectionStatus, setConnectionStatus] =
    useState<ConnectionStatus>('disconnected');
  const [error, setError] = useState<TranslationKey | undefined>();
  const [isJoining, setIsJoining] = useState(false);
  const [privateState, setPrivateState] =
    useState<import('./api/types').PlayerPrivateGameState>();
  const [finalAssignmentRecoveryFailed, setFinalAssignmentRecoveryFailed] =
    useState(false);
  const [privateActionRecoveryFailed, setPrivateActionRecoveryFailed] =
    useState(false);
  const roomInputRef = useRef<HTMLInputElement>(null);
  const recoveredFinalEditPass = useRef<string | undefined>(undefined);
  const recoveredPrivateAction = useRef<string | undefined>(undefined);

  useEffect(() => gameHubConnection.subscribe(setConnectionStatus), []);
  const applySnapshot = useCallback((snapshot: RoomSnapshot): void => {
    setScreen((current) => {
      if (current.kind === 'join') return current;
      if (
        current.snapshot &&
        snapshot.stateVersion < current.snapshot.stateVersion
      )
        return current;
      return snapshot.phase === 'Started' || snapshot.game
        ? { kind: 'game', session: current.session, snapshot }
        : { kind: 'lobby', session: current.session, snapshot };
    });
  }, []);
  const attach = useCallback(
    async (session: PlayerSession): Promise<void> => {
      try {
        applySnapshot(await gameHubConnection.attach(session));
      } catch {
        /* SignalR state announces a recoverable network failure. */
      }
    },
    [applySnapshot],
  );
  const restore = useCallback(
    async (session: PlayerSession): Promise<void> => {
      try {
        const resumed = await resumePlayer(session);
        setPrivateState(resumed.privateState);
        applySnapshot(resumed.snapshot);
        await attach(session);
        setError(undefined);
      } catch (restoreError) {
        if (
          restoreError instanceof PlayerApiError &&
          restoreError.kind === 'invalid-session'
        ) {
          clearPlayerSession();
          setScreen({ kind: 'join' });
          setError('sessionExpired');
        }
      }
    },
    [applySnapshot, attach],
  );
  const recoverFinalEditAssignment = useCallback(
    async (session: PlayerSession): Promise<void> => {
      // SignalR is the normal route. This bounded REST reconciliation covers the
      // valid ordering where a private next-pass assignment reaches the browser
      // before the corresponding public snapshot, and reconnects mid-pass.
      setFinalAssignmentRecoveryFailed(false);
      for (let attempt = 0; attempt < 5; attempt += 1) {
        try {
          const resumed = await resumePlayer(session);
          setPrivateState(resumed.privateState);
          applySnapshot(resumed.snapshot);
          return;
        } catch (recoveryError) {
          if (
            recoveryError instanceof PlayerApiError &&
            recoveryError.kind === 'invalid-session'
          ) {
            clearPlayerSession();
            setScreen({ kind: 'join' });
            setError('sessionExpired');
            return;
          }
          if (attempt < 4)
            await new Promise<void>((resolve) =>
              window.setTimeout(resolve, 500),
            );
        }
      }
      setFinalAssignmentRecoveryFailed(true);
    },
    [applySnapshot],
  );
  const recoverPrivateActionState = useCallback(
    async (session: PlayerSession): Promise<void> => {
      // The server sends the public stage before the private action state on an
      // AttachPlayer call. A browser may therefore legitimately see Drawing
      // first. Reconcile through the authenticated REST resume endpoint instead
      // of guessing eligibility or mounting an unauthorised action form.
      setPrivateActionRecoveryFailed(false);
      for (let attempt = 0; attempt < 3; attempt += 1) {
        try {
          const resumed = await resumePlayer(session);
          setPrivateState(resumed.privateState);
          applySnapshot(resumed.snapshot);
          return;
        } catch (recoveryError) {
          if (
            recoveryError instanceof PlayerApiError &&
            recoveryError.kind === 'invalid-session'
          ) {
            clearPlayerSession();
            setScreen({ kind: 'join' });
            setError('sessionExpired');
            return;
          }
          if (attempt < 2)
            await new Promise<void>((resolve) =>
              window.setTimeout(resolve, 400),
            );
        }
      }
      setPrivateActionRecoveryFailed(true);
    },
    [applySnapshot],
  );
  useEffect(
    () => gameHubConnection.onSnapshot((snapshot) => applySnapshot(snapshot)),
    [applySnapshot],
  );
  useEffect(
    () =>
      gameHubConnection.onGameStarted((snapshot) => {
        applySnapshot(snapshot);
      }),
    [applySnapshot],
  );
  useEffect(
    () =>
      gameHubConnection.onPrivateState((candidate) => {
        setPrivateActionRecoveryFailed(false);
        setPrivateState((current) =>
          current && current.questionInstanceId === candidate.questionInstanceId
            ? {
                ...candidate,
                hasSubmittedTextAnswer:
                  current.hasSubmittedTextAnswer ||
                  candidate.hasSubmittedTextAnswer,
                ownTextAnswerId:
                  candidate.ownTextAnswerId ?? current.ownTextAnswerId,
                hasSubmittedTextAnswerVote:
                  current.hasSubmittedTextAnswerVote ||
                  candidate.hasSubmittedTextAnswerVote,
                hasSubmittedPhotoAnswer:
                  current.hasSubmittedPhotoAnswer ||
                  candidate.hasSubmittedPhotoAnswer,
                ownPhotoAnswerId:
                  candidate.ownPhotoAnswerId ?? current.ownPhotoAnswerId,
                hasSubmittedPhotoAnswerVote:
                  current.hasSubmittedPhotoAnswerVote ||
                  candidate.hasSubmittedPhotoAnswerVote,
                hasSubmittedDrawingAnswer:
                  current.hasSubmittedDrawingAnswer ||
                  candidate.hasSubmittedDrawingAnswer,
                ownDrawingAnswerId:
                  candidate.ownDrawingAnswerId ?? current.ownDrawingAnswerId,
                hasSubmittedDrawingAnswerVote:
                  current.hasSubmittedDrawingAnswerVote ||
                  candidate.hasSubmittedDrawingAnswerVote,
              }
            : candidate,
        );
      }),
    [],
  );
  // Session restoration is an external synchronization action; it must run once
  // after the persisted identity has been loaded into the initial view state.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (initialSession) void restore(initialSession);
  }, [initialSession, restore]);
  const finalEditPass =
    screen.kind === 'game' &&
    screen.snapshot?.game?.stage === 'CollectingFinalEdits'
      ? `${screen.session.roomCode}:${screen.snapshot.game.finalRound?.currentPass ?? 'unknown'}`
      : undefined;
  const finalEditSession =
    screen.kind === 'game' &&
    screen.snapshot?.game?.stage === 'CollectingFinalEdits'
      ? screen.session
      : undefined;
  useEffect(() => {
    if (!finalEditPass || !finalEditSession) {
      recoveredFinalEditPass.current = undefined;
      return;
    }
    if (recoveredFinalEditPass.current === finalEditPass) return;
    recoveredFinalEditPass.current = finalEditPass;
    void recoverFinalEditAssignment(finalEditSession);
  }, [finalEditPass, finalEditSession, recoverFinalEditAssignment]);
  const privateActionGame =
    screen.kind === 'game' ? screen.snapshot?.game : undefined;
  const privateActionQuestionId = privateActionGame
    ? (gameQuestion(privateActionGame)?.instanceId ??
      gameQuestion(privateActionGame)?.id)
    : undefined;
  const privateAction = privateActionGame
    ? privateActionType(privateActionGame.stage)
    : undefined;
  const privateActionKey =
    privateAction && privateActionQuestionId && screen.kind === 'game'
      ? `${screen.session.roomCode}:${privateAction}:${privateActionQuestionId}`
      : undefined;
  useEffect(() => {
    if (
      !privateActionKey ||
      !privateActionQuestionId ||
      screen.kind !== 'game'
    ) {
      recoveredPrivateAction.current = undefined;
      return;
    }
    if (recoveredPrivateAction.current === privateActionKey) return;
    recoveredPrivateAction.current = privateActionKey;
    // Reconcile every new action identity once. SignalR can carry a stale
    // private eligibility flag with the right question id; REST resume is the
    // authoritative paired snapshot/private-state contract for this route.
    const timer = window.setTimeout(
      () => void recoverPrivateActionState(screen.session),
      250,
    );
    return () => window.clearTimeout(timer);
  }, [
    privateActionKey,
    privateActionQuestionId,
    privateState?.questionInstanceId,
    recoverPrivateActionState,
    screen,
  ]);
  useEffect(() => {
    const resumeVisibleSession = () => {
      if (document.visibilityState !== 'visible') return;
      const persisted = loadPlayerSession();
      if (persisted) void restore(persisted);
    };
    document.addEventListener('visibilitychange', resumeVisibleSession);
    return () =>
      document.removeEventListener('visibilitychange', resumeVisibleSession);
  }, [restore]);
  useEffect(() => {
    if (screen.kind === 'join' && error === 'sessionExpired')
      roomInputRef.current?.focus();
  }, [error, screen.kind]);

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    const normalizedCode = normalizeRoomCode(roomCode);
    const normalizedNickname = nickname.trim();
    setRoomCode(normalizedCode);
    setNickname(normalizedNickname);
    const validationError = validate(normalizedCode, normalizedNickname);
    if (validationError) {
      setError(validationError);
      return;
    }
    setError(undefined);
    setIsJoining(true);
    try {
      const joined = await joinRoom(normalizedCode, normalizedNickname);
      const session: PlayerSession = {
        roomCode: joined.roomCode,
        playerId: joined.playerId,
        reconnectToken: joined.reconnectToken,
        nickname: normalizedNickname,
      };
      savePlayerSession(session);
      setPrivateState(joined.privateState);
      setScreen({ kind: 'lobby', session, snapshot: joined.snapshot });
      void attach(session);
    } catch (joinError) {
      setError(
        joinError instanceof PlayerApiError
          ? errorMessage(joinError)
          : 'server',
      );
    } finally {
      setIsJoining(false);
    }
  }

  function hostCreated(
    created: import('./api/types').RoomAccessResponse,
    hostNickname: string,
  ): void {
    const session: PlayerSession = {
      roomCode: created.roomCode,
      playerId: created.playerId,
      reconnectToken: created.reconnectToken,
      nickname: hostNickname,
    };
    setRoomCode(created.roomCode);
    setNickname(hostNickname);
    savePlayerSession(session);
    setPrivateState(created.privateState);
    setError(undefined);
    setScreen({ kind: 'lobby', session, snapshot: created.snapshot });
    void attach(session);
  }

  if (screen.kind === 'game')
    return (
      <GameRouter
        session={screen.session}
        snapshot={screen.snapshot}
        privateState={privateState}
        locale={locale}
        status={connectionStatus}
        t={t}
        onSnapshot={applySnapshot}
        onPrivateState={setPrivateState}
        onRecoverFinalEdit={() =>
          void recoverFinalEditAssignment(screen.session)
        }
        finalEditRecoveryFailed={finalAssignmentRecoveryFailed}
        onRecoverPrivateAction={() => {
          recoveredPrivateAction.current = undefined;
          void recoverPrivateActionState(screen.session);
        }}
        privateActionRecoveryFailed={privateActionRecoveryFailed}
      />
    );
  if (screen.kind === 'lobby')
    return (
      <Lobby
        session={screen.session}
        snapshot={screen.snapshot}
        status={connectionStatus}
        error={error}
        t={t}
        onSnapshot={applySnapshot}
        onError={setError}
        onReconnect={() => void restore(screen.session)}
        onLeave={async () => {
          await gameHubConnection.leaveLobby(screen.session);
          clearPlayerSession();
          setPrivateState(undefined);
          setError(undefined);
          setRoomCode('');
          setNickname('');
          setScreen({ kind: 'join' });
        }}
      />
    );
  if (entryMode === 'host')
    return (
      <HostSetup
        locale={locale}
        t={t}
        error={error}
        onBack={() => {
          setEntryMode('join');
          setError(undefined);
        }}
        onCreated={hostCreated}
        onError={setError}
      />
    );
  return (
    <main className="page-shell">
      <section className="card" aria-labelledby="page-title">
        <p className="eyebrow">{t('title')}</p>
        <div className="entry-actions">
          <button
            type="button"
            className="entry-action"
            onClick={() => {
              setEntryMode('host');
              setError(undefined);
            }}
          >
            {t('hostGame')}
          </button>
        </div>
        <h1 id="page-title">{t('join')}</h1>
        <form onSubmit={submit} noValidate>
          <label htmlFor="roomCode">{t('roomCode')}</label>
          <input
            ref={roomInputRef}
            id="roomCode"
            name="roomCode"
            value={roomCode}
            onChange={(event) =>
              setRoomCode(normalizeRoomCode(event.target.value))
            }
            maxLength={4}
            autoFocus={!initialSession}
            autoCapitalize="characters"
            autoCorrect="off"
            spellCheck="false"
            inputMode="text"
            autoComplete="off"
          />
          <label htmlFor="nickname">{t('nickname')}</label>
          <input
            id="nickname"
            name="nickname"
            value={nickname}
            onChange={(event) => setNickname(event.target.value)}
            maxLength={20}
            autoCapitalize="words"
            autoComplete="nickname"
          />
          {error && (
            <p className="form-error" role="alert">
              {t(error)}
            </p>
          )}
          <button type="submit" disabled={isJoining}>
            {isJoining ? t('joining') : t('join')}
          </button>
        </form>
      </section>
    </main>
  );
}

function Lobby({
  session,
  snapshot,
  status,
  error,
  t,
  onSnapshot,
  onError,
  onReconnect,
  onLeave,
}: {
  session: PlayerSession;
  snapshot?: RoomSnapshot;
  status: ConnectionStatus;
  error?: TranslationKey;
  t: (key: TranslationKey) => string;
  onSnapshot: (snapshot: RoomSnapshot) => void;
  onError: (key: TranslationKey | undefined) => void;
  onReconnect: () => void;
  onLeave: () => Promise<void>;
}) {
  const [pendingPhoto, setPendingPhoto] = useState<Blob>();
  const [previewUrl, setPreviewUrl] = useState<string>();
  const [uploading, setUploading] = useState(false);
  const [confirmingLeave, setConfirmingLeave] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const ownPlayer = snapshot?.players.find(
    (player) => player.id === session.playerId,
  );
  useEffect(
    () => () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    },
    [previewUrl],
  );
  async function choosePhoto(
    event: ChangeEvent<HTMLInputElement>,
  ): Promise<void> {
    const file = event.target.files?.[0];
    if (!file) return;
    try {
      const prepared = await prepareProfilePhoto(file);
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      setPendingPhoto(prepared);
      setPreviewUrl(URL.createObjectURL(prepared));
      onError(undefined);
    } catch (photoError) {
      onError(
        photoError instanceof ProfilePhotoError
          ? photoError.kind === 'unsupported'
            ? 'unsupportedPhoto'
            : photoError.kind === 'too-large'
              ? 'photoTooLarge'
              : 'photoProcessing'
          : 'photoProcessing',
      );
    }
  }
  async function savePhoto(): Promise<void> {
    if (!pendingPhoto) return;
    setUploading(true);
    try {
      onSnapshot(await uploadProfilePhoto(session, pendingPhoto));
      setPendingPhoto(undefined);
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      setPreviewUrl(undefined);
    } catch {
      onError('uploadFailed');
    } finally {
      setUploading(false);
    }
  }
  async function toggleReady(): Promise<void> {
    if (!ownPlayer) return;
    try {
      onSnapshot(await gameHubConnection.setReady(session, !ownPlayer.isReady));
    } catch {
      onError('network');
    }
  }
  async function leave(): Promise<void> {
    setLeaving(true);
    try {
      await onLeave();
    } catch {
      setLeaving(false);
      setConfirmingLeave(false);
      onError('leaveFailed');
    }
  }
  return (
    <main className="page-shell">
      <section className="card lobby-card" aria-live="polite">
        <p className="eyebrow">{t('lobby')}</p>
        <h1>
          {t('room')} {session.roomCode}
        </h1>
        <p className="connection" aria-live="polite">
          {status === 'reconnecting'
            ? t('reconnectingHint')
            : status === 'connected'
              ? t('connectedStatus')
              : status === 'connecting'
                ? t('connecting')
                : t('offline')}
        </p>
        {status === 'disconnected' && (
          <button
            type="button"
            className="secondary-action"
            onClick={onReconnect}
          >
            {t('retry')}
          </button>
        )}
        {error && (
          <p className="form-error" role="alert">
            {t(error)}
          </p>
        )}
        <section className="profile" aria-label={t('profile')}>
          <Avatar
            player={ownPlayer}
            previewUrl={previewUrl}
            version={snapshot?.stateVersion}
            t={t}
          />
          <div>
            <strong>{ownPlayer?.nickname ?? session.nickname}</strong>
            <label className="file-button" htmlFor="profilePhoto">
              {t('choosePhoto')}
            </label>
            <input
              id="profilePhoto"
              className="visually-hidden"
              type="file"
              accept="image/*"
              capture="user"
              onChange={choosePhoto}
            />
            {pendingPhoto && (
              <button
                type="button"
                onClick={() => void savePhoto()}
                disabled={uploading}
              >
                {uploading ? t('uploading') : t('uploadPhoto')}
              </button>
            )}
            {!ownPlayer?.hasProfilePhoto && (
              <p className="hint">{t('photoRequired')}</p>
            )}
          </div>
        </section>
        <section className="players" aria-label={t('players')}>
          <h2>{t('players')}</h2>
          <ul>
            {snapshot?.players.map((player) => (
              <li key={player.id}>
                <Avatar player={player} version={snapshot.stateVersion} t={t} />
                <span>
                  {player.nickname}
                  {player.id === session.playerId ? ` (${t('you')})` : ''}
                </span>
                <span className={player.isReady ? 'ready' : 'waiting'}>
                  {player.isReady ? t('ready') : t('waitingStatus')}
                </span>
              </li>
            ))}
          </ul>
        </section>
        <div className="lobby-actions">
          <button
            type="button"
            onClick={() => void toggleReady()}
            disabled={
              !ownPlayer || !ownPlayer.hasProfilePhoto || status !== 'connected'
            }
          >
            {ownPlayer?.isReady ? t('notReady') : t('ready')}
          </button>
          <div className="leave-room">
          {confirmingLeave ? (
            <div role="alertdialog" aria-label={t('leaveRoomConfirm')}>
              <p>{t('leaveRoomConfirm')}</p>
              <button
                type="button"
                className="secondary-action"
                onClick={() => setConfirmingLeave(false)}
                disabled={leaving}
              >
                {t('cancel')}
              </button>
              <button
                type="button"
                onClick={() => void leave()}
                disabled={leaving}
              >
                {leaving ? t('leavingRoom') : t('leaveRoom')}
              </button>
            </div>
          ) : (
            <button
              type="button"
              className="secondary-action"
              onClick={() => setConfirmingLeave(true)}
              disabled={leaving}
            >
              {t('leaveRoom')}
            </button>
          )}
          </div>
        </div>
      </section>
    </main>
  );
}

function Avatar({
  player,
  previewUrl,
  version,
  t,
}: {
  player?: PublicPlayer;
  previewUrl?: string;
  version?: number;
  t: (key: TranslationKey) => string;
}) {
  const url = previewUrl ?? profilePhotoUrl(player, version);
  return url ? (
    <img
      className="avatar"
      src={url}
      alt={`${t('avatarOf')}: ${player?.nickname ?? t('you')}`}
    />
  ) : (
    <span
      className="avatar avatar--placeholder"
      aria-label={`${t('avatarOf')}: ${player?.nickname ?? t('you')}`}
    >
      👤
    </span>
  );
}
function profilePhotoUrl(
  player?: PublicPlayer,
  version?: number,
): string | undefined {
  if (!player?.profilePhotoUrl) return undefined;
  if (version === undefined) return player.profilePhotoUrl;
  const separator = player.profilePhotoUrl.includes('?') ? '&' : '?';
  return `${player.profilePhotoUrl}${separator}v=${encodeURIComponent(String(version))}`;
}
function normalizeRoomCode(value: string): string {
  return value.trim().toUpperCase().slice(0, 4);
}
function validate(
  roomCode: string,
  nickname: string,
): TranslationKey | undefined {
  if (!roomCode) return 'roomRequired';
  if (roomCode.length !== 4) return 'roomLength';
  if (!nickname) return 'nicknameRequired';
  if (nickname.length < 2 || nickname.length > 20) return 'nicknameLength';
  return undefined;
}
function errorMessage(error: PlayerApiError): TranslationKey {
  switch (error.kind) {
    case 'not-found':
      return 'roomNotFound';
    case 'started':
      return 'roomStarted';
    case 'nickname-taken':
      return 'nicknameTaken';
    case 'room-full':
      return 'roomFull';
    case 'validation':
      return 'invalid';
    case 'network':
      return 'network';
    case 'invalid-session':
      return 'sessionExpired';
    default:
      return 'server';
  }
}
