import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GameRouter } from './GameRouter';
import { translations } from '../translations';
import type { PlayerPrivateGameState, RoomSnapshot } from '../api/types';

const hub = vi.hoisted(() => ({ submitPlayerSelection: vi.fn(), submitTextAnswer: vi.fn(), submitTextAnswerVote: vi.fn(), getRoomSnapshot: vi.fn(), serverNow: vi.fn(() => Date.now()) }));
vi.mock('../realtime/gameHubConnection', () => ({ gameHubConnection: hub }));

const session = { roomCode: 'AB12', playerId: 'p1', reconnectToken: 'token', nickname: 'Wojtek' };
const privateState: PlayerPrivateGameState = { playerId: 'p1', questionInstanceId: 'q1', hasSubmittedTextAnswer: false, hasSubmittedTextAnswerVote: false, isEligibleForTextAnswerVote: true };
const baseSnapshot: RoomSnapshot = { roomCode: 'AB12', phase: 'Started', stateVersion: 4, players: [
  { id: 'p1', nickname: 'Wojtek', isHost: true, isReady: true, isConnected: true, hasProfilePhoto: true, score: 0 },
  { id: 'p2', nickname: 'Ania', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
  { id: 'p3', nickname: 'Kamil', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
] };
const game = (stage: string) => ({ stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1, questionsInCurrentRound: 3, stageEndsAtUtc: new Date(Date.now() + 12_000).toISOString(), question: { id: 'definition', instanceId: 'q1', text: { pl: 'Kto rozbawia ekipę?' } } });
const renderGame = (snapshot: RoomSnapshot, state = privateState) => render(<GameRouter session={session} snapshot={snapshot} privateState={state} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);

describe('GameRouter', () => {
  beforeEach(() => { sessionStorage.clear(); vi.clearAllMocks(); hub.getRoomSnapshot.mockResolvedValue(baseSnapshot); });

  it('renders eligible player choices and submits a selection exactly once', async () => {
    renderGame({ ...baseSnapshot, game: game('CollectingPlayerSelections') });
    fireEvent.click(screen.getByRole('button', { name: 'Ania' }));
    const submit = screen.getByRole('button', { name: 'Wyślij odpowiedź' }); fireEvent.click(submit); fireEvent.click(submit);
    await waitFor(() => expect(hub.submitPlayerSelection).toHaveBeenCalledTimes(1));
    expect(hub.submitPlayerSelection).toHaveBeenCalledWith(session, 'p2', 'q1', expect.any(String));
    expect(screen.getByText('Odpowiedź wysłana')).toBeInTheDocument();
  });

  it('submits text with the same idempotency identity after a retry', async () => {
    hub.submitTextAnswer.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(undefined);
    renderGame({ ...baseSnapshot, game: game('CollectingTextAnswers') });
    fireEvent.change(screen.getByLabelText('Twoja odpowiedź'), { target: { value: '  Bardzo śmieszna odpowiedź  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Wyślij odpowiedź' }));
    await screen.findByRole('alert'); fireEvent.click(screen.getByRole('button', { name: 'Spróbuj ponownie' }));
    await waitFor(() => expect(hub.submitTextAnswer).toHaveBeenCalledTimes(2));
    expect(hub.submitTextAnswer.mock.calls[0][2]).toBe('q1'); expect(hub.submitTextAnswer.mock.calls[0][3]).toBe(hub.submitTextAnswer.mock.calls[1][3]);
    expect(screen.getByText('Odpowiedź wysłana')).toBeInTheDocument();
  });

  it('uses only authoritative text vote options and confirms a submitted vote', async () => {
    hub.submitTextAnswerVote.mockResolvedValue(undefined);
    renderGame({ ...baseSnapshot, game: { ...game('CollectingTextAnswerVotes'), textResults: { questionInstanceId: 'q1', answeredPlayers: 3, requiredPlayers: 3, votingOptions: [{ answerId: 'a1', text: 'Opcja pierwsza' }, { answerId: 'a2', text: 'Opcja druga' }] } } });
    expect(screen.getByText('Opcja pierwsza')).toBeInTheDocument(); fireEvent.click(screen.getByLabelText('Opcja druga')); fireEvent.click(screen.getByRole('button', { name: 'Oddaj głos' }));
    await waitFor(() => expect(hub.submitTextAnswerVote).toHaveBeenCalledWith(session, 'a2', 'q1', expect.any(String)));
    expect(screen.getByText('Głos oddany')).toBeInTheDocument();
  });

  it('restores submitted text and vote state from the private reconnect payload', () => {
    const submitted = { ...privateState, hasSubmittedTextAnswer: true, hasSubmittedTextAnswerVote: true };
    const { rerender } = renderGame({ ...baseSnapshot, game: game('CollectingTextAnswers') }, submitted);
    expect(screen.getByText('Odpowiedź wysłana')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, game: { ...game('CollectingTextAnswerVotes'), textResults: { questionInstanceId: 'q1', answeredPlayers: 3, requiredPlayers: 3, votingOptions: [] } } }} privateState={submitted} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);
    expect(screen.getByText('Głos oddany')).toBeInTheDocument();
  });

  it('renders a countdown without advancing the stage and safely handles unsupported media stages', () => {
    const { rerender } = renderGame({ ...baseSnapshot, game: game('CollectingTextAnswers') });
    expect(screen.getByText(/Pozostały czas/)).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, game: game('CollectingPhotoAnswers') }} privateState={privateState} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);
    expect(screen.getByText('Ten typ pytania nie jest jeszcze obsługiwany.')).toBeInTheDocument();
  });
});
