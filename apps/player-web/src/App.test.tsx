import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import {
  createRoom,
  fetchPackages,
  joinRoom,
  PlayerApiError,
  resumePlayer,
  uploadProfilePhoto,
} from './api/playerApi';
import type { RoomSnapshot } from './api/types';
import { prepareProfilePhoto, ProfilePhotoError } from './media/profilePhoto';
import {
  clearPlayerSession,
  loadPlayerSession,
  savePlayerSession,
} from './session/playerSession';

const realtime = vi.hoisted(() => ({
  attach: vi.fn(),
  setReady: vi.fn(),
  leaveLobby: vi.fn(),
  snapshotListener: undefined as ((snapshot: RoomSnapshot) => void) | undefined,
  startedListener: undefined as ((snapshot: RoomSnapshot) => void) | undefined,
  privateListener: undefined as ((state: unknown) => void) | undefined,
}));
vi.mock('./api/playerApi', () => ({
  createRoom: vi.fn(),
  fetchPackages: vi.fn(),
  joinRoom: vi.fn(),
  resumePlayer: vi.fn(),
  uploadProfilePhoto: vi.fn(),
  PlayerApiError: class PlayerApiError extends Error {
    kind: string;
    constructor(kind: string) {
      super(kind);
      this.kind = kind;
    }
  },
}));
vi.mock('./media/profilePhoto', () => ({
  prepareProfilePhoto: vi.fn(),
  ProfilePhotoError: class ProfilePhotoError extends Error {
    kind: string;
    constructor(kind: string) {
      super(kind);
      this.kind = kind;
    }
  },
}));
vi.mock('./realtime/gameHubConnection', () => ({
  gameHubConnection: {
    subscribe: (listener: (value: 'connected') => void) => {
      listener('connected');
      return () => undefined;
    },
    onSnapshot: (listener: (snapshot: RoomSnapshot) => void) => {
      realtime.snapshotListener = listener;
      return () => undefined;
    },
    onGameStarted: (listener: (snapshot: RoomSnapshot) => void) => {
      realtime.startedListener = listener;
      return () => undefined;
    },
    onPrivateState: (listener: (state: unknown) => void) => {
      realtime.privateListener = listener;
      return () => undefined;
    },
    attach: realtime.attach,
    setReady: realtime.setReady,
    leaveLobby: realtime.leaveLobby,
    serverNow: () => Date.now(),
    getRoomSnapshot: vi.fn(),
    submitPlayerSelection: vi.fn(),
    submitTextAnswer: vi.fn(),
    submitTextAnswerVote: vi.fn(),
  },
}));

const session = {
  roomCode: 'AB12',
  playerId: '11111111-1111-1111-1111-111111111111',
  reconnectToken: 'reconnect-token',
  nickname: 'Wojtek',
};
const privateState = {
  playerId: session.playerId,
  questionInstanceId: null,
  hasSubmittedTextAnswer: false,
  ownTextAnswerId: null,
  hasSubmittedTextAnswerVote: false,
  isEligibleForTextAnswerVote: false,
};
const snapshot: RoomSnapshot = {
  roomCode: 'AB12',
  phase: 'Lobby',
  stateVersion: 3,
  players: [
    {
      id: session.playerId,
      nickname: 'Wojtek',
      isHost: true,
      isReady: false,
      isConnected: true,
      hasProfilePhoto: true,
      profilePhotoUrl: '/avatars/wojtek.jpg',
      score: 0,
    },
    {
      id: '2',
      nickname: 'Ania',
      isHost: false,
      isReady: true,
      isConnected: true,
      hasProfilePhoto: true,
      profilePhotoUrl: '/avatars/ania.jpg',
      score: 0,
    },
    {
      id: '3',
      nickname: 'Kamil',
      isHost: false,
      isReady: false,
      isConnected: false,
      hasProfilePhoto: false,
      profilePhotoUrl: null,
      score: 0,
    },
  ],
};
const joined = { ...session, snapshot, privateState };

describe('Web Player lobby', () => {
  beforeEach(() => {
    clearPlayerSession();
    vi.mocked(createRoom).mockReset();
    vi.mocked(fetchPackages)
      .mockReset()
      .mockResolvedValue([
        {
          key: 'starter',
          name: { pl: 'Startowy', en: 'Starter' },
          description: { pl: '', en: '' },
          categoryCount: 1,
          minimumSupportedRounds: 1,
          maximumSupportedRounds: 1,
          isDefault: true,
        },
      ]);
    vi.mocked(joinRoom).mockReset();
    vi.mocked(resumePlayer).mockReset();
    vi.mocked(uploadProfilePhoto).mockReset();
    vi.mocked(prepareProfilePhoto).mockReset();
    realtime.attach.mockReset().mockResolvedValue(snapshot);
    realtime.setReady.mockReset().mockResolvedValue({
      ...snapshot,
      players: [
        { ...snapshot.players[0], isReady: true },
        ...snapshot.players.slice(1),
      ],
    });
    realtime.leaveLobby.mockReset().mockResolvedValue(snapshot);
    realtime.snapshotListener = undefined;
    realtime.startedListener = undefined;
    window.history.replaceState({}, '', '/play/?room=ab12');
  });
  it('prefills the room code and validates a missing nickname', async () => {
    render(<App />);
    expect(screen.getByLabelText('Kod pokoju')).toHaveValue('AB12');
    await userEvent.click(
      screen.getByRole('button', { name: 'Dołącz do gry' }),
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Wpisz nick');
  });
  it('normalizes deep-link and manual room codes with the backend-compatible trim and uppercase rules', async () => {
    render(<App />);
    expect(screen.getByLabelText('Kod pokoju')).toHaveValue('AB12');
    await userEvent.clear(screen.getByLabelText('Kod pokoju'));
    await userEvent.type(screen.getByLabelText('Kod pokoju'), 'a1bi-cd');
    expect(screen.getByLabelText('Kod pokoju')).toHaveValue('A1BI');
  });
  it('creates a host as a normal persisted player using the public room and package contracts', async () => {
    vi.mocked(createRoom).mockResolvedValue(joined);
    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Hostuj grę' }));
    expect(await screen.findByText('Startowy')).toBeInTheDocument();
    expect(fetchPackages).toHaveBeenCalledOnce();
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Wojtek');
    await userEvent.click(screen.getByLabelText('Startowy'));
    await userEvent.click(screen.getByLabelText('Odpowiedź tekstowa'));
    fireEvent.change(screen.getByLabelText('Liczba rund'), {
      target: { value: '2' },
    });
    await userEvent.click(screen.getByRole('button', { name: 'Utwórz pokój' }));
    expect(createRoom).toHaveBeenCalledWith(
      expect.objectContaining({
        nickname: 'Wojtek',
        selectedPackageKeys: ['starter'],
        enabledQuestionTypes: ['PlayerSelection', 'TextAnswer'],
        settings: expect.objectContaining({
          roundCount: 2,
          finalDrawingPasses: 3,
        }),
      }),
    );
    expect(await screen.findByText('Ania')).toBeInTheDocument();
    expect(loadPlayerSession()).toEqual(session);
    expect(realtime.attach).toHaveBeenCalledWith(session);
  });
  it('requires one enabled host question type and hides final passes when the final round is disabled', async () => {
    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Hostuj grę' }));
    await screen.findByText('Startowy');
    await userEvent.click(screen.getByLabelText('Wybór gracza'));
    await userEvent.click(screen.getByRole('button', { name: 'Utwórz pokój' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Wpisz nick');
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Ada');
    await userEvent.click(screen.getByRole('button', { name: 'Utwórz pokój' }));
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Wybierz co najmniej jeden typ pytania',
    );
    expect(
      screen.getByLabelText('Liczba finałowych przekazań'),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('Włącz rundę finałową'));
    expect(
      screen.queryByLabelText('Liczba finałowych przekazań'),
    ).not.toBeInTheDocument();
  });
  it('joins, stores the player session, and renders all lobby players', async () => {
    vi.mocked(joinRoom).mockResolvedValue(joined);
    render(<App />);
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Wojtek');
    await userEvent.click(
      screen.getByRole('button', { name: 'Dołącz do gry' }),
    );
    expect(await screen.findByText('Ania')).toBeInTheDocument();
    expect(screen.getByText('Kamil')).toBeInTheDocument();
    expect(screen.getAllByRole('img')).toHaveLength(3);
    expect(screen.getAllByText('Oczekuje')).toHaveLength(2);
    expect(joinRoom).toHaveBeenCalledWith('AB12', 'Wojtek');
    expect(loadPlayerSession()).toEqual(session);
  });
  it('leaves only after confirmation and clears the persisted player session after server acknowledgement', async () => {
    vi.mocked(joinRoom).mockResolvedValue(joined);
    render(<App />);
    await userEvent.type(screen.getByLabelText('Twój nick'), 'Wojtek');
    await userEvent.click(
      screen.getByRole('button', { name: 'Dołącz do gry' }),
    );
    await screen.findByText('Ania');
    await userEvent.click(
      screen.getByRole('button', { name: 'Wyjdź z pokoju' }),
    );
    expect(screen.getByRole('alertdialog')).toHaveTextContent(
      'Na pewno chcesz wyjść',
    );
    await userEvent.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Wyjdź z pokoju',
      }),
    );
    await waitFor(() =>
      expect(realtime.leaveLobby).toHaveBeenCalledWith(session),
    );
    expect(loadPlayerSession()).toBeUndefined();
    expect(screen.getByLabelText('Kod pokoju')).toHaveValue('');
  });
  it('uses the existing SetReady contract and accepts authoritative snapshots', async () => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    render(<App />);
    await screen.findByText('Ania');
    await userEvent.click(screen.getByRole('button', { name: 'Gotowy' }));
    expect(realtime.setReady).toHaveBeenCalledWith(session, true);
    expect(await screen.findAllByText('Gotowy')).toHaveLength(2);
    realtime.snapshotListener?.({
      ...snapshot,
      stateVersion: 4,
      players: [
        { ...snapshot.players[0], isReady: false },
        ...snapshot.players.slice(1),
      ],
    });
    expect(
      await screen.findByRole('button', { name: 'Gotowy' }),
    ).toBeInTheDocument();
  });
  it('prepares, previews, and uploads a selected profile photo', async () => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    vi.mocked(prepareProfilePhoto).mockResolvedValue(
      new Blob(['photo'], { type: 'image/jpeg' }),
    );
    vi.mocked(uploadProfilePhoto).mockResolvedValue(snapshot);
    render(<App />);
    await screen.findByText('Ania');
    const file = new File(['photo'], 'profile.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Wybierz zdjęcie'), {
      target: { files: [file] },
    });
    await userEvent.click(
      await screen.findByRole('button', { name: 'Zapisz zdjęcie' }),
    );
    expect(uploadProfilePhoto).toHaveBeenCalledWith(session, expect.any(Blob));
  });
  it.each([
    ['unsupported', 'Wybierz plik graficzny'],
    ['too-large', 'Zdjęcie jest zbyt duże'],
  ])('reports controlled profile error %s', async (kind, message) => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    vi.mocked(prepareProfilePhoto).mockRejectedValue(
      new ProfilePhotoError(kind as 'unsupported' | 'too-large'),
    );
    render(<App />);
    await screen.findByText('Ania');
    fireEvent.change(screen.getByLabelText('Wybierz zdjęcie'), {
      target: {
        files: [
          new File(['bad'], 'bad.bin', { type: 'application/octet-stream' }),
        ],
      },
    });
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
  });
  it('restores the same player without a new join and clears expired sessions', async () => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    const view = render(<App />);
    await screen.findByText('Ania');
    expect(resumePlayer).toHaveBeenCalledWith(session);
    expect(realtime.attach).toHaveBeenCalledWith(session);
    expect(joinRoom).not.toHaveBeenCalled();
    view.unmount();
    clearPlayerSession();
    vi.mocked(resumePlayer).mockRejectedValue(
      new PlayerApiError('invalid-session'),
    );
    savePlayerSession(session);
    render(<App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Sesja wygasła');
    expect(loadPlayerSession()).toBeUndefined();
  });
  it('routes a GameStarted snapshot to the authoritative gameplay view', async () => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    render(<App />);
    await screen.findByText('Ania');
    realtime.startedListener?.({
      ...snapshot,
      phase: 'Started',
      game: {
        stage: 'CollectingTextAnswers',
        currentRoundNumber: 1,
        totalRounds: 1,
        currentQuestionNumber: 1,
        questionsInCurrentRound: 1,
        question: {
          id: 'question',
          instanceId: 'instance',
          text: { pl: 'Pytanie testowe' },
        },
      },
    });
    realtime.privateListener?.({
      ...privateState,
      questionInstanceId: 'instance',
    });
    expect(await screen.findByText('Pytanie testowe')).toBeInTheDocument();
  });
  it('recovers a mobile Drawing route when the public stage arrives before its private action state', async () => {
    const drawingSnapshot: RoomSnapshot = {
      ...snapshot,
      phase: 'Started',
      stateVersion: 4,
      game: {
        stage: 'CollectingDrawingAnswers',
        currentRoundNumber: 1,
        totalRounds: 1,
        currentQuestionNumber: 1,
        questionsInCurrentRound: 1,
        question: {
          id: 'drawing-definition',
          instanceId: 'drawing-instance',
          text: { pl: 'Narysuj coś' },
        },
      },
    };
    const drawingPrivate = {
      ...privateState,
      questionInstanceId: 'drawing-instance',
      isEligibleForDrawingAnswer: true,
      hasSubmittedDrawingAnswer: false,
    };
    savePlayerSession(session);
    vi.mocked(resumePlayer)
      .mockResolvedValueOnce({
        player: snapshot.players[0],
        snapshot,
        privateState,
      })
      .mockResolvedValueOnce({
        player: snapshot.players[0],
        snapshot: drawingSnapshot,
        privateState: drawingPrivate,
      });
    render(<App />);
    await screen.findByText('Ania');
    await act(async () => {
      realtime.startedListener?.(drawingSnapshot);
    });
    expect(screen.getByText('Ładowanie gry…')).toBeInTheDocument();
    expect(
      await screen.findByRole(
        'img',
        { name: 'Obszar rysowania' },
        { timeout: 2_000 },
      ),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Wybierz kolor' }));
    expect(
      screen.getAllByRole('button', {
        name: /Czarny|Biały|Czerwony|Niebieski|Zielony|Żółty|Pomarańczowy|Fioletowy|Różowy|Brązowy/,
      }),
    ).toHaveLength(10);
    expect(
      screen.getByRole('button', { name: 'Wyślij rysunek' }),
    ).toBeDisabled();
    expect(screen.queryByText('Ładowanie gry…')).not.toBeInTheDocument();
  });
  it('restores Completed after refresh and ignores a stale snapshot that would leave the final screen', async () => {
    const completed = {
      ...snapshot,
      phase: 'Completed',
      stateVersion: 12,
      game: {
        stage: 'Completed',
        currentRoundNumber: 1,
        totalRounds: 1,
        currentQuestionNumber: 1,
        questionsInCurrentRound: 1,
        ranking: [
          { playerId: session.playerId, score: 1500, rank: 1 },
          { playerId: '2', score: 0, rank: 2 },
          { playerId: '3', score: 0, rank: 2 },
        ],
      },
    };
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot: completed,
      privateState,
    });
    render(<App />);
    expect(
      await screen.findByRole('heading', { name: 'Gra zakończona' }),
    ).toBeInTheDocument();
    realtime.snapshotListener?.({
      ...snapshot,
      phase: 'Started',
      stateVersion: 11,
      game: {
        stage: 'CollectingTextAnswers',
        currentRoundNumber: 1,
        totalRounds: 1,
        currentQuestionNumber: 1,
        questionsInCurrentRound: 1,
        question: {
          id: 'question',
          instanceId: 'instance',
          text: { pl: 'Stare pytanie' },
        },
      },
    });
    expect(screen.queryByText('Stare pytanie')).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Gra zakończona' }),
    ).toBeInTheDocument();
  });
  it('resumes the persisted player when the mobile tab becomes visible again', async () => {
    savePlayerSession(session);
    vi.mocked(resumePlayer).mockResolvedValue({
      player: snapshot.players[0],
      snapshot,
      privateState,
    });
    render(<App />);
    await screen.findByText('Ania');
    vi.mocked(resumePlayer).mockClear();
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      value: 'visible',
    });
    document.dispatchEvent(new Event('visibilitychange'));
    await waitFor(() => expect(resumePlayer).toHaveBeenCalledWith(session));
  });
});
