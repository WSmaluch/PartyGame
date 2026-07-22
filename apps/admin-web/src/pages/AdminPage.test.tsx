import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { getHealth } from '../api/healthApi';
import { gameHubConnection } from '../realtime/gameHubConnection';

vi.mock('../api/healthApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/healthApi')>();
  return { ...original, getHealth: vi.fn() };
});

vi.mock('../realtime/gameHubConnection', () => ({
  gameHubConnection: {
    subscribe: vi.fn((listener: (status: string) => void) => {
      listener('connected');
      return vi.fn();
    }),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    ping: vi
      .fn()
      .mockResolvedValue({ status: 'pong', utcTime: '2026-07-20T12:01:00Z' }),
  },
}));

const health = {
  status: 'ok',
  service: 'PartyGame.Api',
  version: '1.0.0.0',
  utcTime: '2026-07-20T12:00:00Z',
};

function renderApp() {
  return render(
    <MemoryRouter initialEntries={['/admin']}>
      <App />
    </MemoryRouter>,
  );
}

describe('AdminPage', () => {
  beforeEach(() => {
    vi.mocked(getHealth).mockReset();
    vi.mocked(gameHubConnection.start).mockClear();
    vi.mocked(gameHubConnection.ping).mockClear();
  });

  it('renderuje ekran i stan ładowania', () => {
    vi.mocked(getHealth).mockReturnValue(new Promise(() => undefined));
    renderApp();
    expect(
      screen.getByRole('heading', { name: /PartyGame Admin/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Sprawdzanie', { selector: '.status-pill' }),
    ).toBeInTheDocument();
  });

  it('pokazuje dane health, SignalR i placeholdery modułów', async () => {
    vi.mocked(getHealth).mockResolvedValue(health);
    renderApp();
    expect(await screen.findByText('1.0.0.0')).toBeInTheDocument();
    expect(screen.getByText('Połączony')).toBeInTheDocument();
    expect(await screen.findByText(/pong · 2026/)).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Pytania' }),
    ).toBeInTheDocument();
    expect(gameHubConnection.ping).toHaveBeenCalledOnce();
  });

  it('pokazuje błąd połączenia i status offline', async () => {
    vi.mocked(getHealth).mockRejectedValue(new Error('Backend niedostępny'));
    renderApp();
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Backend niedostępny',
    );
    expect(screen.getByText('Offline')).toBeInTheDocument();
  });

  it('pozwala ręcznie ponowić żądanie', async () => {
    vi.mocked(getHealth)
      .mockRejectedValueOnce(new Error('Błąd'))
      .mockResolvedValueOnce(health);
    renderApp();
    await screen.findByRole('alert');
    await userEvent.click(
      screen.getByRole('button', { name: 'Ponów połączenie' }),
    );
    await waitFor(() => expect(getHealth).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('Online')).toBeInTheDocument();
  });
});
