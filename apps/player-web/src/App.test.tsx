import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { joinRoom, PlayerApiError } from './api/playerApi';
import { clearPlayerSession, loadPlayerSession } from './session/playerSession';

vi.mock('./api/playerApi', () => ({ joinRoom: vi.fn(), PlayerApiError: class PlayerApiError extends Error { kind: string; constructor(kind: string) { super(kind); this.kind = kind; } } }));
vi.mock('./realtime/gameHubConnection', () => ({ gameHubConnection: { subscribe: (listener: (value: 'disconnected') => void) => { listener('disconnected'); return () => undefined; }, onSnapshot: () => () => undefined, attach: vi.fn().mockResolvedValue({ roomCode: 'AB12', phase: 'Lobby', stateVersion: 1, players: [] }) } }));

const joined = { roomCode: 'AB12', playerId: '11111111-1111-1111-1111-111111111111', reconnectToken: 'reconnect-token', snapshot: { roomCode: 'AB12', phase: 'Lobby', stateVersion: 1, players: [] } };

describe('join form', () => {
  beforeEach(() => { clearPlayerSession(); vi.mocked(joinRoom).mockReset(); window.history.replaceState({}, '', '/play/?room=ab12'); });
  it('prefills a room query parameter and validates required values', async () => {
    render(<App />);
    expect(screen.getByLabelText('Kod pokoju')).toHaveValue('AB12');
    await userEvent.click(screen.getByRole('button', { name: 'Dołącz do gry' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Wpisz nick');
  });

  it('joins, saves the session, and shows the waiting screen', async () => {
    vi.mocked(joinRoom).mockResolvedValue(joined);
    render(<App />);
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Wojtek');
    await userEvent.click(screen.getByRole('button', { name: 'Dołącz do gry' }));
    expect(await screen.findByText('Dołączono do gry')).toBeInTheDocument();
    expect(joinRoom).toHaveBeenCalledWith('AB12', 'Wojtek');
    expect(loadPlayerSession()).toMatchObject({ roomCode: 'AB12', nickname: 'Wojtek', reconnectToken: 'reconnect-token' });
  });

  it.each([
    ['not-found', 'Nie znaleziono pokoju'], ['started', 'Do tego pokoju nie można już dołączyć'], ['network', 'Brak połączenia z serwerem'], ['validation', 'Dane są nieprawidłowe'],
  ])('shows a translated %s error', async (kind, message) => {
    vi.mocked(joinRoom).mockRejectedValue(new PlayerApiError(kind as 'not-found' | 'started' | 'network' | 'validation'));
    render(<App />);
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Wojtek');
    await userEvent.click(screen.getByRole('button', { name: 'Dołącz do gry' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
  });
});
