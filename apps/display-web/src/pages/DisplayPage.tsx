import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { apiConfig } from '../api/apiConfig';
import { getHealth, HealthApiError, lastCorrelationId } from '../api/healthApi';
import { getRoomSnapshot, profilePhotoUrl } from '../api/roomApi';
import type { HealthResponse, RoomPlayer, RoomSnapshot } from '../api/types';
import { StatusPill } from '../components/StatusPill';
import { PlayerJoinQrCode } from '../components/PlayerJoinQrCode';
import { GameScreens } from '../components/gameStages/GameScreens';
import { gameHubConnection } from '../realtime/gameHubConnection';
import type { GameHubStatus, HubPingResponse } from '../realtime/types';

const roomStorageKey = 'partygame.display.roomCode';
const allowedCodeCharacters = /[^A-HJ-NP-Z2-9]/g;
const hubLabels: Record<GameHubStatus, string> = {
  disconnected: 'Rozłączony', connecting: 'Łączenie…', connected: 'Połączony',
  reconnecting: 'Ponowne łączenie…', error: 'Błąd połączenia',
};

type ViewState = 'code' | 'connecting' | 'lobby' | 'started' | 'replaced';

export function DisplayPage() {
  const [savedCode] = useState(() => sessionStorage.getItem(roomStorageKey) ?? '');
  const [roomCode, setRoomCode] = useState(savedCode);
  const [view, setView] = useState<ViewState>(savedCode ? 'connecting' : 'code');
  const [snapshot, setSnapshot] = useState<RoomSnapshot>();
  const [error, setError] = useState<string>();
  const [health, setHealth] = useState<HealthResponse>();
  const [hubStatus, setHubStatus] = useState<GameHubStatus>('disconnected');
  const [lastPing, setLastPing] = useState<HubPingResponse>();
  const [lastConnectedAt, setLastConnectedAt] = useState<string>();
  const [lastReconnectAt, setLastReconnectAt] = useState<string>();
  const [diagnosticsLoading, setDiagnosticsLoading] = useState(false);
  const abortController = useRef<AbortController | undefined>(undefined);
  const latestVersion = useRef(-1);

  const applySnapshot = useCallback((candidate: RoomSnapshot) => {
    if (candidate.stateVersion <= latestVersion.current) return;
    latestVersion.current = candidate.stateVersion;
    setSnapshot(candidate);
    setView(candidate.phase === 'Lobby' ? 'lobby' : 'started');
  }, []);

  const checkDiagnostics = useCallback(async () => {
    setDiagnosticsLoading(true);
    try {
      const response = await getHealth();
      setHealth(response);
      await gameHubConnection.start();
      setLastPing(await gameHubConnection.ping());
    } catch (diagnosticError) {
      if (!(diagnosticError instanceof HealthApiError && diagnosticError.kind === 'cancelled')) {
        setError(diagnosticError instanceof Error ? diagnosticError.message : 'Nieznany błąd połączenia.');
      }
    } finally { setDiagnosticsLoading(false); }
  }, []);

  const connectToRoom = useCallback(async (code: string) => {
    const normalized = code.toUpperCase().replace(allowedCodeCharacters, '').slice(0, 4);
    if (normalized.length !== 4) { setError('Wpisz poprawny 4-znakowy kod pokoju.'); return; }
    abortController.current?.abort();
    const controller = new AbortController();
    abortController.current = controller;
    setView('connecting');
    setError(undefined);
    try {
      const restSnapshot = await getRoomSnapshot(normalized, controller.signal);
      await gameHubConnection.start();
      const attachedSnapshot = await gameHubConnection.attachDisplay(normalized);
      sessionStorage.setItem(roomStorageKey, normalized);
      setRoomCode(normalized);
      applySnapshot(restSnapshot);
      applySnapshot(attachedSnapshot);
    } catch (connectionError) {
      if (connectionError instanceof DOMException && connectionError.name === 'AbortError') return;
      setView('code');
      setError(connectionError instanceof Error ? connectionError.message : 'Nie udało się dołączyć do pokoju.');
    }
  }, [applySnapshot]);

  useEffect(() => {
    const subscriptions = [
      gameHubConnection.subscribe((status) => {
        setHubStatus(status);
        if (status === 'connected') setLastConnectedAt(new Date().toISOString());
        if (status === 'reconnecting') setLastReconnectAt(new Date().toISOString());
      }),
      gameHubConnection.onSnapshot(applySnapshot),
      gameHubConnection.onRoomStarted(applySnapshot),
      gameHubConnection.onDisplayReplaced(() => {
        sessionStorage.removeItem(roomStorageKey);
        gameHubConnection.forgetAttachment();
        setSnapshot(undefined);
        setView('replaced');
      }),
    ];
    const initialCheck = window.setTimeout(() => void checkDiagnostics(), 0);
    const initialRoom = savedCode
      ? window.setTimeout(() => void connectToRoom(savedCode), 0)
      : undefined;
    return () => {
      window.clearTimeout(initialCheck);
      if (initialRoom) window.clearTimeout(initialRoom);
      abortController.current?.abort();
      subscriptions.forEach((unsubscribe) => unsubscribe());
    };
  }, [applySnapshot, checkDiagnostics, connectToRoom, savedCode]);

  const submit = (event: FormEvent) => { event.preventDefault(); void connectToRoom(roomCode); };
  const changeRoom = () => {
    sessionStorage.removeItem(roomStorageKey);
    gameHubConnection.forgetAttachment();
    latestVersion.current = -1;
    setSnapshot(undefined); setError(undefined); setRoomCode(''); setView('code');
  };

  return (
    <main className="display-shell">
      <div className="confetti confetti--one" aria-hidden="true" />
      <div className="confetti confetti--two" aria-hidden="true" />
      <section
        className="hero-card"
        data-testid="display-state-version"
        data-state-version={snapshot?.stateVersion ?? ''}
      >
        <p className="eyebrow">Ekran gry</p>
        <h1>PartyGame</h1>

        {error && <div className="error-banner" role="alert">{error}</div>}
        {view === 'code' && <CodeEntry roomCode={roomCode} setRoomCode={setRoomCode} submit={submit} />}
        {view === 'connecting' && <Connecting retry={() => void connectToRoom(roomCode)} />}
        {view === 'lobby' && snapshot && <Lobby snapshot={snapshot} changeRoom={changeRoom} />}
        {view === 'started' && snapshot && <Started snapshot={snapshot} />}
        {view === 'replaced' && (
          <div className="central-message" role="alert">
            <h2>Ten ekran został zastąpiony</h2>
            <p>Inny ekran połączył się z tym pokojem. Możesz wpisać nowy kod.</p>
            <button type="button" onClick={() => setView('code')}>Wpisz kod</button>
          </div>
        )}

        <details className="diagnostics">
          <summary>Diagnostyka połączenia</summary>
          <div className="diagnostics-grid">
            <article><span>SignalR</span><StatusPill label={hubLabels[hubStatus]} state={hubStatus === 'connected' ? 'good' : hubStatus === 'error' ? 'bad' : 'pending'} /></article>
            <article><span>Health</span><strong>{health?.status ?? '—'}</strong></article>
            <article><span>Wersja backendu</span><strong>{health?.version ?? '—'}</strong></article>
            <article><span>Wersja Display</span><strong>{apiConfig.applicationVersion}</strong></article>
            <article><span>Commit Display</span><strong>{apiConfig.commitHash}</strong></article>
            <article><span>Ostatnie połączenie</span><strong>{lastConnectedAt ?? '—'}</strong></article>
            <article><span>Ostatni reconnect</span><strong>{lastReconnectAt ?? '—'}</strong></article>
            <article><span>stateVersion</span><strong>{snapshot?.stateVersion ?? '—'}</strong></article>
            <article><span>Correlation ID</span><strong>{lastCorrelationId() || '—'}</strong></article>
            <article><span>Ostatni Ping</span><strong>{lastPing ? `${lastPing.status} · ${lastPing.utcTime}` : '—'}</strong></article>
          </div>
          <div className="server-address"><span>Serwer</span><code>{apiConfig.baseUrl}</code></div>
          <button type="button" onClick={() => void checkDiagnostics()} disabled={diagnosticsLoading}>Sprawdź ponownie</button>
        </details>
      </section>
    </main>
  );
}

function CodeEntry({ roomCode, setRoomCode, submit }: { roomCode: string; setRoomCode: (value: string) => void; submit: (event: FormEvent) => void }) {
  return <form className="code-entry" onSubmit={submit}>
    <h2>Połącz ekran z pokojem</h2>
    <label htmlFor="room-code">Kod pokoju</label>
    <input id="room-code" autoFocus autoComplete="off" inputMode="text" maxLength={4} value={roomCode}
      onChange={(event) => setRoomCode(event.target.value.toUpperCase().replace(allowedCodeCharacters, '').slice(0, 4))}
      placeholder="ABCD" aria-describedby="room-code-help" />
    <p id="room-code-help">Kod znajdziesz w lobby na telefonie hosta.</p>
    <button type="submit" disabled={roomCode.length !== 4}>Połącz ekran</button>
  </form>;
}

function Connecting({ retry }: { retry: () => void }) {
  return <div className="central-message" aria-live="polite">
    <div className="spinner" aria-hidden="true" /><h2>Łączenie z pokojem…</h2>
    <p>Sprawdzamy pokój i uruchamiamy aktualizacje na żywo.</p>
    <button type="button" onClick={retry}>Spróbuj ponownie</button>
  </div>;
}

function Lobby({ snapshot, changeRoom }: { snapshot: RoomSnapshot; changeRoom: () => void }) {
  return <div className="lobby">
    <div className="room-heading"><span>Kod pokoju</span><strong>{snapshot.roomCode}</strong></div>
    <PlayerJoinQrCode roomCode={snapshot.roomCode} />
    <p className="lobby-count">Gracze: {snapshot.players.length}/{snapshot.maximumPlayers}</p>
    <div className="player-grid">
      {snapshot.players.map((player) => <PlayerCard key={player.id} player={player} version={snapshot.stateVersion} />)}
    </div>
    <p className="lobby-hint">Dołącz telefonem, zrób zdjęcie profilowe i oznacz gotowość.</p>
    <button className="secondary-action" type="button" onClick={changeRoom}>Zmień pokój</button>
  </div>;
}

function PlayerCard({ player, version }: { player: RoomPlayer; version: number }) {
  const photo = profilePhotoUrl(player.profilePhotoUrl);
  return <article className={`player-card ${player.isReady ? 'player-card--ready' : ''}`}>
    {photo ? <img src={`${photo}${photo.includes('?') ? '&' : '?'}v=${version}`} alt={`Zdjęcie profilowe: ${player.nickname}`} />
      : <div className="photo-placeholder" aria-label="Brak zdjęcia profilowego">?</div>}
    <h3>{player.nickname}</h3>
    <div className="badges">
      {player.isHost && <span className="host-badge">Host</span>}
      <span className={player.isConnected ? 'connected' : 'offline'}>{player.isConnected ? 'Online' : 'Offline'}</span>
      <span className={player.isReady ? 'ready' : 'waiting'}>{player.isReady ? 'Gotowy/a' : 'Czeka'}</span>
    </div>
  </article>;
}

function Started({ snapshot }: { snapshot: RoomSnapshot }) {
  return <GameScreens snapshot={snapshot} />;
}
