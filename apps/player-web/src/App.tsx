import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent, FormEvent } from 'react';
import { joinRoom, PlayerApiError, resumePlayer, uploadProfilePhoto } from './api/playerApi';
import type { PlayerSession, PublicPlayer, RoomSnapshot } from './api/types';
import { prepareProfilePhoto, ProfilePhotoError } from './media/profilePhoto';
import { gameHubConnection, type ConnectionStatus } from './realtime/gameHubConnection';
import { clearPlayerSession, loadPlayerSession, savePlayerSession } from './session/playerSession';
import { preferredLocale, translations, type Locale, type TranslationKey } from './translations';

type Screen = { kind: 'join' } | { kind: 'lobby'; session: PlayerSession; snapshot?: RoomSnapshot } | { kind: 'game-started'; session: PlayerSession; snapshot?: RoomSnapshot };

export default function App() {
  const [locale] = useState<Locale>(preferredLocale);
  const t = (key: TranslationKey) => translations[locale][key];
  const initialRoom = useMemo(() => normalizeRoomCode(new URLSearchParams(window.location.search).get('room') ?? ''), []);
  const initialSession = useMemo(() => loadPlayerSession(), []);
  const [roomCode, setRoomCode] = useState(initialSession?.roomCode ?? initialRoom);
  const [nickname, setNickname] = useState(initialSession?.nickname ?? '');
  const [screen, setScreen] = useState<Screen>(() => initialSession ? { kind: 'lobby', session: initialSession } : { kind: 'join' });
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected');
  const [error, setError] = useState<TranslationKey | undefined>();
  const [isJoining, setIsJoining] = useState(false);
  const roomInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => gameHubConnection.subscribe(setConnectionStatus), []);
  const applySnapshot = useCallback((snapshot: RoomSnapshot): void => {
    setScreen((current) => current.kind === 'join' ? current : snapshot.phase === 'Started'
      ? { kind: 'game-started', session: current.session, snapshot }
      : { kind: 'lobby', session: current.session, snapshot });
  }, []);
  const attach = useCallback(async (session: PlayerSession): Promise<void> => {
    try { applySnapshot(await gameHubConnection.attach(session)); } catch { /* SignalR state announces a recoverable network failure. */ }
  }, [applySnapshot]);
  const restore = useCallback(async (session: PlayerSession): Promise<void> => {
    try {
      const resumed = await resumePlayer(session);
      applySnapshot(resumed.snapshot);
      await attach(session);
      setError(undefined);
    } catch (restoreError) {
      if (restoreError instanceof PlayerApiError && restoreError.kind === 'invalid-session') {
        clearPlayerSession(); setScreen({ kind: 'join' }); setError('sessionExpired');
      }
    }
  }, [applySnapshot, attach]);
  useEffect(() => gameHubConnection.onSnapshot((snapshot) => applySnapshot(snapshot)), [applySnapshot]);
  useEffect(() => gameHubConnection.onGameStarted((snapshot) => {
    setScreen((current) => current.kind === 'join' ? current : { kind: 'game-started', session: current.session, snapshot });
  }), []);
  // Session restoration is an external synchronization action; it must run once
  // after the persisted identity has been loaded into the initial view state.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { if (initialSession) void restore(initialSession); }, [initialSession, restore]);
  useEffect(() => {
    if (screen.kind === 'join' && error === 'sessionExpired') roomInputRef.current?.focus();
  }, [error, screen.kind]);

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    const normalizedCode = normalizeRoomCode(roomCode); const normalizedNickname = nickname.trim();
    setRoomCode(normalizedCode); setNickname(normalizedNickname);
    const validationError = validate(normalizedCode, normalizedNickname);
    if (validationError) { setError(validationError); return; }
    setError(undefined); setIsJoining(true);
    try {
      const joined = await joinRoom(normalizedCode, normalizedNickname);
      const session: PlayerSession = { roomCode: joined.roomCode, playerId: joined.playerId, reconnectToken: joined.reconnectToken, nickname: normalizedNickname };
      savePlayerSession(session); setScreen({ kind: 'lobby', session, snapshot: joined.snapshot }); void attach(session);
    } catch (joinError) { setError(joinError instanceof PlayerApiError ? errorMessage(joinError) : 'server'); } finally { setIsJoining(false); }
  }

  if (screen.kind === 'game-started') return <GameStarted session={screen.session} snapshot={screen.snapshot} t={t} />;
  if (screen.kind === 'lobby') return <Lobby session={screen.session} snapshot={screen.snapshot} status={connectionStatus} error={error} t={t} onSnapshot={applySnapshot} onError={setError} onReconnect={() => void restore(screen.session)} />;
  return <main className="page-shell"><section className="card" aria-labelledby="page-title"><p className="eyebrow">{t('title')}</p><h1 id="page-title">{t('join')}</h1><form onSubmit={submit} noValidate><label htmlFor="roomCode">{t('roomCode')}</label><input ref={roomInputRef} id="roomCode" name="roomCode" value={roomCode} onChange={(event) => setRoomCode(normalizeRoomCode(event.target.value))} maxLength={4} autoCapitalize="characters" autoCorrect="off" spellCheck="false" inputMode="text" autoComplete="off" /><label htmlFor="nickname">{t('nickname')}</label><input id="nickname" name="nickname" value={nickname} onChange={(event) => setNickname(event.target.value)} maxLength={20} autoCapitalize="words" autoComplete="nickname" />{error && <p className="form-error" role="alert">{t(error)}</p>}<button type="submit" disabled={isJoining}>{isJoining ? t('joining') : t('join')}</button></form></section></main>;
}

function Lobby({ session, snapshot, status, error, t, onSnapshot, onError, onReconnect }: { session: PlayerSession; snapshot?: RoomSnapshot; status: ConnectionStatus; error?: TranslationKey; t: (key: TranslationKey) => string; onSnapshot: (snapshot: RoomSnapshot) => void; onError: (key: TranslationKey | undefined) => void; onReconnect: () => void }) {
  const [pendingPhoto, setPendingPhoto] = useState<Blob>(); const [previewUrl, setPreviewUrl] = useState<string>(); const [uploading, setUploading] = useState(false); const ownPlayer = snapshot?.players.find((player) => player.id === session.playerId);
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl); }, [previewUrl]);
  async function choosePhoto(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.target.files?.[0]; if (!file) return;
    try { const prepared = await prepareProfilePhoto(file); if (previewUrl) URL.revokeObjectURL(previewUrl); setPendingPhoto(prepared); setPreviewUrl(URL.createObjectURL(prepared)); onError(undefined); } catch (photoError) { onError(photoError instanceof ProfilePhotoError ? photoError.kind === 'unsupported' ? 'unsupportedPhoto' : photoError.kind === 'too-large' ? 'photoTooLarge' : 'photoProcessing' : 'photoProcessing'); }
  }
  async function savePhoto(): Promise<void> {
    if (!pendingPhoto) return; setUploading(true);
    try { onSnapshot(await uploadProfilePhoto(session, pendingPhoto)); setPendingPhoto(undefined); if (previewUrl) URL.revokeObjectURL(previewUrl); setPreviewUrl(undefined); } catch { onError('uploadFailed'); } finally { setUploading(false); }
  }
  async function toggleReady(): Promise<void> {
    if (!ownPlayer) return; try { onSnapshot(await gameHubConnection.setReady(session, !ownPlayer.isReady)); } catch { onError('network'); }
  }
  return <main className="page-shell"><section className="card lobby-card" aria-live="polite"><p className="eyebrow">{t('lobby')}</p><h1>{t('room')} {session.roomCode}</h1><p className="connection" aria-live="polite">{status === 'reconnecting' ? t('reconnectingHint') : status === 'connected' ? t('connectedStatus') : status === 'connecting' ? t('connecting') : t('offline')}</p>{status === 'disconnected' && <button type="button" className="secondary-action" onClick={onReconnect}>{t('retry')}</button>}{error && <p className="form-error" role="alert">{t(error)}</p>}<section className="profile" aria-label={t('profile')}><Avatar player={ownPlayer} previewUrl={previewUrl} version={snapshot?.stateVersion} t={t} /><div><strong>{ownPlayer?.nickname ?? session.nickname}</strong><label className="file-button" htmlFor="profilePhoto">{t('choosePhoto')}</label><input id="profilePhoto" className="visually-hidden" type="file" accept="image/*" capture="user" onChange={choosePhoto} />{pendingPhoto && <button type="button" onClick={() => void savePhoto()} disabled={uploading}>{uploading ? t('uploading') : t('uploadPhoto')}</button>}{!ownPlayer?.hasProfilePhoto && <p className="hint">{t('photoRequired')}</p>}</div></section><section className="players" aria-label={t('players')}><h2>{t('players')}</h2><ul>{snapshot?.players.map((player) => <li key={player.id}><Avatar player={player} version={snapshot.stateVersion} t={t} /><span>{player.nickname}{player.id === session.playerId ? ` (${t('you')})` : ''}</span><span className={player.isReady ? 'ready' : 'waiting'}>{player.isReady ? t('ready') : t('waitingStatus')}</span></li>)}</ul></section><button type="button" onClick={() => void toggleReady()} disabled={!ownPlayer || !ownPlayer.hasProfilePhoto || status !== 'connected'}>{ownPlayer?.isReady ? t('notReady') : t('ready')}</button></section></main>;
}

function Avatar({ player, previewUrl, version, t }: { player?: PublicPlayer; previewUrl?: string; version?: number; t: (key: TranslationKey) => string }) { const url = previewUrl ?? profilePhotoUrl(player, version); return url ? <img className="avatar" src={url} alt={`${t('avatarOf')}: ${player?.nickname ?? t('you')}`} /> : <span className="avatar avatar--placeholder" aria-label={`${t('avatarOf')}: ${player?.nickname ?? t('you')}`}>👤</span>; }
function GameStarted({ session, snapshot, t }: { session: PlayerSession; snapshot?: RoomSnapshot; t: (key: TranslationKey) => string }) { return <main className="page-shell"><section className="card waiting" aria-live="polite"><p className="eyebrow">{t('title')}</p><h1>{t('gameStarted')}</h1><p>{t('room')}: {snapshot?.roomCode ?? session.roomCode}</p><p className="waiting-message">{t('gameStartedHint')}</p></section></main>; }
function profilePhotoUrl(player?: PublicPlayer, version?: number): string | undefined { if (!player?.profilePhotoUrl) return undefined; if (version === undefined) return player.profilePhotoUrl; const separator = player.profilePhotoUrl.includes('?') ? '&' : '?'; return `${player.profilePhotoUrl}${separator}v=${encodeURIComponent(String(version))}`; }
function normalizeRoomCode(value: string): string { return value.trim().toUpperCase().slice(0, 4); }
function validate(roomCode: string, nickname: string): TranslationKey | undefined { if (!roomCode) return 'roomRequired'; if (roomCode.length !== 4) return 'roomLength'; if (!nickname) return 'nicknameRequired'; if (nickname.length < 2 || nickname.length > 20) return 'nicknameLength'; return undefined; }
function errorMessage(error: PlayerApiError): TranslationKey { switch (error.kind) { case 'not-found': return 'roomNotFound'; case 'started': return 'roomStarted'; case 'validation': return 'invalid'; case 'network': return 'network'; case 'invalid-session': return 'sessionExpired'; default: return 'server'; } }
