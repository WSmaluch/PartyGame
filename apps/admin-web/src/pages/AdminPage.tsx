import { useCallback, useEffect, useRef, useState } from 'react';
import { apiConfig } from '../api/apiConfig';
import { getHealth, HealthApiError } from '../api/healthApi';
import type { HealthResponse } from '../api/types';
import { StatusPill } from '../components/StatusPill';
import { ContentPackages } from '../components/ContentPackages';
import { gameHubConnection } from '../realtime/gameHubConnection';
import { clearOperatorToken } from '../api/operatorSession';
import type { GameHubStatus, HubPingResponse } from '../realtime/types';

const modules = [
  'Pakiety',
  'Kategorie',
  'Pytania',
  'Polecenia fotograficzne',
  'Naklejki',
  'Aktywne pokoje',
  'Historia gier',
];

const hubLabels: Record<GameHubStatus, string> = {
  disconnected: 'Rozłączony',
  connecting: 'Łączenie',
  connected: 'Połączony',
  reconnecting: 'Ponowne łączenie',
  error: 'Błąd',
};

export function AdminPage() {
  const [health, setHealth] = useState<HealthResponse>();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [hubStatus, setHubStatus] = useState<GameHubStatus>('disconnected');
  const [lastPing, setLastPing] = useState<HubPingResponse>();
  const abortController = useRef<AbortController | undefined>(undefined);

  const checkConnection = useCallback(async () => {
    abortController.current?.abort();
    const controller = new AbortController();
    abortController.current = controller;
    setIsLoading(true);
    setError(undefined);
    try {
      const response = await getHealth(controller.signal);
      setHealth(response);
      await gameHubConnection.start();
      setLastPing(await gameHubConnection.ping());
    } catch (connectionError) {
      if (
        connectionError instanceof HealthApiError &&
        connectionError.kind === 'cancelled'
      )
        return;
      setError(
        connectionError instanceof Error
          ? connectionError.message
          : 'Nieznany błąd.',
      );
    } finally {
      if (!controller.signal.aborted) setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    const unsubscribe = gameHubConnection.subscribe(setHubStatus);
    const initialCheck = window.setTimeout(() => void checkConnection(), 0);
    return () => {
      window.clearTimeout(initialCheck);
      abortController.current?.abort();
      unsubscribe();
      void gameHubConnection.stop();
    };
  }, [checkConnection]);

  const apiOnline = health?.status === 'ok' && !error;
  const hubTone =
    hubStatus === 'connected'
      ? 'good'
      : hubStatus === 'error'
        ? 'bad'
        : 'pending';

  return (
    <div className="admin-shell">
      <aside>
        <div className="brand-mark">PG</div>
        <h1>
          PartyGame <span>Admin</span>
        </h1>
        <p>Panel operacyjny</p>
      </aside>
      <main>
        <header>
          <div>
            <p className="eyebrow">Środowisko lokalne</p>
            <h2>Stan systemu</h2>
          </div>
          <button
            type="button"
            onClick={() => void checkConnection()}
            disabled={isLoading}
          >
            {isLoading ? 'Sprawdzanie…' : 'Ponów połączenie'}
          </button>
          <button type="button" onClick={clearOperatorToken}>Wyloguj operatora</button>
        </header>

        {error && (
          <div className="error-banner" role="alert">
            {error}
          </div>
        )}

        <section className="status-panel" aria-live="polite">
          <article>
            <span>Backend</span>
            <StatusPill
              label={
                isLoading ? 'Sprawdzanie' : apiOnline ? 'Online' : 'Offline'
              }
              state={isLoading ? 'pending' : apiOnline ? 'good' : 'bad'}
            />
          </article>
          <article>
            <span>SignalR</span>
            <StatusPill label={hubLabels[hubStatus]} state={hubTone} />
          </article>
          <article>
            <span>Wersja</span>
            <strong>{health?.version ?? '—'}</strong>
          </article>
          <article>
            <span>Czas UTC</span>
            <strong>{health?.utcTime ?? '—'}</strong>
          </article>
          <article className="wide">
            <span>Adres API</span>
            <code>{apiConfig.baseUrl}</code>
          </article>
          <article className="wide">
            <span>Ostatni SignalR Ping</span>
            <strong>
              {lastPing ? `${lastPing.status} · ${lastPing.utcTime}` : '—'}
            </strong>
          </article>
        </section>

        <ContentPackages />

        <section className="modules">
          <div className="section-title">
            <h2>Moduły</h2>
            <span>Etap 1+</span>
          </div>
          <div className="module-grid">
            {modules.map((module, index) => (
              <article key={module}>
                <span>{String(index + 1).padStart(2, '0')}</span>
                <h3>{module}</h3>
                <p>Moduł zostanie uruchomiony w kolejnym etapie.</p>
                <button type="button" disabled>
                  Wkrótce
                </button>
              </article>
            ))}
          </div>
        </section>
      </main>
    </div>
  );
}
