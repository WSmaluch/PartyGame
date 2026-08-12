import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { joinRoom, PlayerApiError } from './api/playerApi';
import type { PlayerSession, RoomSnapshot } from './api/types';
import { gameHubConnection, type ConnectionStatus } from './realtime/gameHubConnection';
import { loadPlayerSession, savePlayerSession } from './session/playerSession';
import { preferredLocale, translations, type Locale, type TranslationKey } from './translations';

type Screen = { kind: 'join' } | { kind: 'waiting'; session: PlayerSession; snapshot?: RoomSnapshot };

export default function App() {
  const [locale] = useState<Locale>(preferredLocale);
  const t = (key: TranslationKey) => translations[locale][key];
  const initialRoom = useMemo(() => normalizeRoomCode(new URLSearchParams(window.location.search).get('room') ?? ''), []);
  const initialSession = useMemo(() => loadPlayerSession(), []);
  const [roomCode, setRoomCode] = useState(initialSession?.roomCode ?? initialRoom);
  const [nickname, setNickname] = useState(initialSession?.nickname ?? '');
  const [screen, setScreen] = useState<Screen>(() => initialSession ? { kind: 'waiting', session: initialSession } : { kind: 'join' });
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected');
  const [error, setError] = useState<TranslationKey | undefined>();
  const [isJoining, setIsJoining] = useState(false);

  useEffect(() => gameHubConnection.subscribe(setConnectionStatus), []);
  useEffect(() => gameHubConnection.onSnapshot((snapshot) => {
    setScreen((current) => current.kind === 'waiting' ? { ...current, snapshot } : current);
  }), []);
  useEffect(() => {
    if (initialSession) void attach(initialSession);
  }, [initialSession]);

  async function attach(session: PlayerSession): Promise<void> {
    try {
      const snapshot = await gameHubConnection.attach(session);
      setScreen({ kind: 'waiting', session, snapshot });
    } catch {
      // A successful HTTP join remains valid. The translated disconnected state
      // gives the player a controlled indication while SignalR retries later.
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    const normalizedCode = normalizeRoomCode(roomCode);
    const normalizedNickname = nickname.trim();
    setRoomCode(normalizedCode);
    setNickname(normalizedNickname);
    const validationError = validate(normalizedCode, normalizedNickname);
    if (validationError) { setError(validationError); return; }
    setError(undefined);
    setIsJoining(true);
    try {
      const joined = await joinRoom(normalizedCode, normalizedNickname);
      const session: PlayerSession = { roomCode: joined.roomCode, playerId: joined.playerId, reconnectToken: joined.reconnectToken, nickname: normalizedNickname };
      savePlayerSession(session);
      setScreen({ kind: 'waiting', session, snapshot: joined.snapshot });
      void attach(session);
    } catch (joinError) {
      setError(joinError instanceof PlayerApiError ? errorMessage(joinError) : 'server');
    } finally { setIsJoining(false); }
  }

  if (screen.kind === 'waiting') return <WaitingScreen session={screen.session} snapshot={screen.snapshot} status={connectionStatus} t={t} />;
  return (
    <main className="page-shell">
      <section className="card" aria-labelledby="page-title">
        <p className="eyebrow">{t('title')}</p>
        <h1 id="page-title">{t('join')}</h1>
        <form onSubmit={submit} noValidate>
          <label htmlFor="roomCode">{t('roomCode')}</label>
          <input id="roomCode" name="roomCode" value={roomCode} onChange={(event) => setRoomCode(normalizeRoomCode(event.target.value))} maxLength={4} autoCapitalize="characters" autoCorrect="off" spellCheck="false" inputMode="text" autoComplete="off" />
          <label htmlFor="nickname">{t('nickname')}</label>
          <input id="nickname" name="nickname" value={nickname} onChange={(event) => setNickname(event.target.value)} maxLength={20} autoCapitalize="words" autoComplete="nickname" />
          {error && <p className="form-error" role="alert">{t(error)}</p>}
          <button type="submit" disabled={isJoining}>{isJoining ? t('joining') : t('join')}</button>
        </form>
      </section>
    </main>
  );
}

function WaitingScreen({ session, snapshot, status, t }: { session: PlayerSession; snapshot?: RoomSnapshot; status: ConnectionStatus; t: (key: TranslationKey) => string }) {
  return <main className="page-shell"><section className="card waiting" aria-live="polite">
    <p className="eyebrow">{t('title')}</p><h1>{t('joined')}</h1>
    <dl><div><dt>{t('room')}</dt><dd>{session.roomCode}</dd></div><div><dt>{t('player')}</dt><dd>{session.nickname}</dd></div><div><dt>{t('connection')}</dt><dd><span className={`status status--${status}`}>{t(status)}</span></dd></div></dl>
    {snapshot && snapshot.players.length > 0 && <section className="players" aria-label={t('players')}><h2>{t('players')}</h2><ul>{snapshot.players.map((player) => <li key={player.id}>{player.nickname}</li>)}</ul></section>}
    <p className="waiting-message">{t('waiting')}</p>
  </section></main>;
}

function normalizeRoomCode(value: string): string { return value.trim().toUpperCase().slice(0, 4); }
function validate(roomCode: string, nickname: string): TranslationKey | undefined {
  if (!roomCode) return 'roomRequired';
  if (roomCode.length !== 4) return 'roomLength';
  if (!nickname) return 'nicknameRequired';
  if (nickname.length < 2 || nickname.length > 20) return 'nicknameLength';
  return undefined;
}
function errorMessage(error: PlayerApiError): TranslationKey {
  switch (error.kind) { case 'not-found': return 'roomNotFound'; case 'started': return 'roomStarted'; case 'validation': return 'invalid'; case 'network': return 'network'; default: return 'server'; }
}
