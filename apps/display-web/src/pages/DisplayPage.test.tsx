import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { getHealth } from '../api/healthApi';
import { getRoomSnapshot } from '../api/roomApi';
import type { RoomSnapshot } from '../api/types';
import { gameHubConnection } from '../realtime/gameHubConnection';

vi.mock('../api/healthApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/healthApi')>();
  return { ...original, getHealth: vi.fn() };
});
vi.mock('../api/roomApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/roomApi')>();
  return { ...original, getRoomSnapshot: vi.fn() };
});

let snapshotListener: (snapshot: RoomSnapshot) => void;
let startedListener: (snapshot: RoomSnapshot) => void;
let replacedListener: () => void;

vi.mock('../realtime/gameHubConnection', () => ({
  gameHubConnection: {
    subscribe: vi.fn((listener: (status: string) => void) => { listener('connected'); return vi.fn(); }),
    onSnapshot: vi.fn((listener: (snapshot: RoomSnapshot) => void) => { snapshotListener = listener; return vi.fn(); }),
    onRoomStarted: vi.fn((listener: (snapshot: RoomSnapshot) => void) => { startedListener = listener; return vi.fn(); }),
    onDisplayReplaced: vi.fn((listener: () => void) => { replacedListener = listener; return vi.fn(); }),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    attachDisplay: vi.fn(),
    forgetAttachment: vi.fn(),
    ping: vi.fn().mockResolvedValue({ status: 'pong', utcTime: '2026-07-20T12:01:00Z' }),
  },
}));

const health = { status: 'ok', service: 'PartyGame.Api', version: '1.0.0.0', utcTime: '2026-07-20T12:00:00Z' };
const player = { id: '0dc81d35-c68d-47c6-aebb-5e86407a1bb0', nickname: 'Ola', isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: '/uploads/ola.jpg', score: 0 };
const lobby: RoomSnapshot = {
  roomCode: 'ABCD', phase: 'Lobby', stateVersion: 3, displayConnected: true,
  minimumPlayers: 3, maximumPlayers: 8, canStart: false,
  settings: { roundCount: 4, questionsPerRound: 5, playerSelectionSeconds: 20, textAnswerSeconds: 40, votingSeconds: 20, photoSeconds: 45, drawingSeconds: 90, resultPresentationSeconds: 8, finalRoundEnabled: true, finalDrawingPasses: 3 },
  players: [player], createdAtUtc: '2026-07-20T12:00:00Z', startedAtUtc: null,
};

function renderApp() {
  return render(<MemoryRouter initialEntries={['/display']}><App /></MemoryRouter>);
}

describe('DisplayPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.mocked(getHealth).mockResolvedValue(health);
    vi.mocked(getRoomSnapshot).mockResolvedValue(lobby);
    vi.mocked(gameHubConnection.attachDisplay).mockResolvedValue(lobby);
    vi.clearAllMocks();
  });

  it('normalizuje kod i aktywuje połączenie dopiero dla czterech znaków', async () => {
    renderApp();
    const input = screen.getByLabelText('Kod pokoju');
    await userEvent.type(input, 'a1bi-cd');
    expect(input).toHaveValue('ABCD');
    await userEvent.click(screen.getByRole('button', { name: 'Połącz ekran' }));
    expect(await screen.findByText('Ola')).toBeInTheDocument();
    expect(gameHubConnection.attachDisplay).toHaveBeenCalledWith('ABCD');
    expect(sessionStorage.getItem('partygame.display.roomCode')).toBe('ABCD');
  });

  it('pokazuje błąd pokoju i pozwala ponowić próbę', async () => {
    vi.mocked(getRoomSnapshot).mockRejectedValueOnce(new Error('Pokój nie istnieje'));
    renderApp();
    await userEvent.type(screen.getByLabelText('Kod pokoju'), 'ABCD');
    await userEvent.click(screen.getByRole('button', { name: 'Połącz ekran' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Pokój nie istnieje');
    await userEvent.click(screen.getByRole('button', { name: 'Połącz ekran' }));
    expect(await screen.findByText('Ola')).toBeInTheDocument();
  });

  it('odtwarza pokój po odświeżeniu i ignoruje starszy stateVersion', async () => {
    sessionStorage.setItem('partygame.display.roomCode', 'ABCD');
    renderApp();
    expect(await screen.findByText('Ola')).toBeInTheDocument();
    act(() => snapshotListener({ ...lobby, stateVersion: 2, players: [{ ...player, nickname: 'Stare dane' }] }));
    expect(screen.queryByText('Stare dane')).not.toBeInTheDocument();
    act(() => snapshotListener({ ...lobby, stateVersion: 4, players: [{ ...player, nickname: 'Nowe dane', isReady: true }] }));
    expect(screen.getByText('Nowe dane')).toBeInTheDocument();
    expect(screen.getByText('Gotowy/a')).toBeInTheDocument();
  });

  it('obsługuje RoomStarted', async () => {
    sessionStorage.setItem('partygame.display.roomCode', 'ABCD');
    renderApp();
    await screen.findByText('Ola');
    act(() => startedListener({ ...lobby, phase: 'Started', stateVersion: 5 }));
    expect(screen.getByRole('heading', { name: 'Gra rozpoczęta!' })).toBeInTheDocument();
  });

  it('czyści zapis po DisplayReplaced i pokazuje jasny komunikat', async () => {
    sessionStorage.setItem('partygame.display.roomCode', 'ABCD');
    renderApp();
    await screen.findByText('Ola');
    act(() => replacedListener());
    expect(screen.getByRole('alert')).toHaveTextContent('Ten ekran został zastąpiony');
    expect(sessionStorage.getItem('partygame.display.roomCode')).toBeNull();
    expect(gameHubConnection.forgetAttachment).toHaveBeenCalled();
  });

  it('udostępnia health i rzeczywistą metodę Ping w diagnostyce', async () => {
    renderApp();
    await waitFor(() => expect(gameHubConnection.ping).toHaveBeenCalled());
    expect(screen.getByText('1.0.0.0')).toBeInTheDocument();
    expect(screen.getByText(/pong · 2026/)).toBeInTheDocument();
  });
});
