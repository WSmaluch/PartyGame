import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GameRouter } from './GameRouter';
import { translations } from '../translations';
import type { PlayerPrivateGameState, RoomSnapshot } from '../api/types';

const hub = vi.hoisted(() => ({ submitPlayerSelection: vi.fn(), submitTextAnswer: vi.fn(), submitTextAnswerVote: vi.fn(), submitPhotoAnswerVote: vi.fn(), submitDrawingAnswerVote: vi.fn(), getRoomSnapshot: vi.fn(), playAgain: vi.fn(), serverNow: vi.fn(() => Date.now()) }));
const media = vi.hoisted(() => ({ preparePhotoAnswer: vi.fn(), drawingPng: vi.fn(), GameMediaError: class GameMediaError extends Error { readonly kind: string; constructor(kind: string) { super(kind); this.kind = kind; } } }));
const api = vi.hoisted(() => ({ uploadPhotoAnswer: vi.fn(), uploadDrawingAnswer: vi.fn(), uploadFinalSelfie: vi.fn(), uploadFinalEdit: vi.fn(), submitFinalVote: vi.fn() }));
vi.mock('../realtime/gameHubConnection', () => ({ gameHubConnection: hub }));
vi.mock('../media/gameMedia', () => media);
vi.mock('../api/playerApi', () => api);
vi.mock('./DrawingCanvas', () => ({ DrawingCanvas: ({ onCanvas, onInkChange, labels }: { onCanvas: (canvas: HTMLCanvasElement) => void; onInkChange: (hasInk: boolean) => void; labels: { canvas: string } }) => <button type="button" onClick={() => { onCanvas(document.createElement('canvas')); onInkChange(true); }}>{labels.canvas}</button> }));

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
  beforeEach(() => {
    sessionStorage.clear(); vi.clearAllMocks(); hub.getRoomSnapshot.mockResolvedValue(baseSnapshot); api.uploadPhotoAnswer.mockResolvedValue({ playerPrivateGameState: privateState, roomSnapshot: baseSnapshot }); api.uploadDrawingAnswer.mockResolvedValue({ playerPrivateGameState: privateState, roomSnapshot: baseSnapshot });
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:preview') }); Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
  });

  it('renders eligible player choices and submits a selection exactly once', async () => {
    renderGame({ ...baseSnapshot, game: game('CollectingPlayerSelections') });
    fireEvent.click(screen.getByRole('button', { name: 'Ania' }));
    const submit = screen.getByRole('button', { name: 'Wyślij odpowiedź' }); fireEvent.click(submit); fireEvent.click(submit);
    await waitFor(() => expect(hub.submitPlayerSelection).toHaveBeenCalledTimes(1));
    expect(hub.submitPlayerSelection).toHaveBeenCalledWith(session, 'p2', 'q1', expect.any(String));
    expect(screen.getByText('Odpowiedź wysłana')).toBeInTheDocument();
  });

  it('keeps the current player as a valid PlayerSelection voting option', async () => {
    renderGame({ ...baseSnapshot, game: game('CollectingPlayerSelections') });
    fireEvent.click(screen.getByRole('button', { name: 'Wojtek' }));
    fireEvent.click(screen.getByRole('button', { name: 'Wyślij odpowiedź' }));
    await waitFor(() => expect(hub.submitPlayerSelection).toHaveBeenCalledWith(session, 'p1', 'q1', expect.any(String)));
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

  it('processes a selected photo and retries its idempotent media submission', async () => {
    const photo = new File(['image'], 'photo.jpg', { type: 'image/jpeg' }); const prepared = new Blob(['jpeg'], { type: 'image/jpeg' });
    media.preparePhotoAnswer.mockResolvedValue(prepared); api.uploadPhotoAnswer.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({ playerPrivateGameState: privateState, roomSnapshot: baseSnapshot });
    renderGame({ ...baseSnapshot, game: game('CollectingPhotoAnswers') });
    const chooser = screen.getByLabelText('Zrób lub wybierz zdjęcie') as HTMLInputElement;
    expect(chooser.accept).toBe('image/*'); expect(chooser.getAttribute('capture')).toBe('environment');
    fireEvent.change(chooser, { target: { files: [photo] } });
    await screen.findByAltText('Podgląd zdjęcia');
    const submit = screen.getByRole('button', { name: 'Wyślij zdjęcie' }); fireEvent.click(submit); fireEvent.click(submit);
    await screen.findByRole('alert'); fireEvent.click(screen.getByRole('button', { name: 'Spróbuj ponownie' }));
    await waitFor(() => expect(api.uploadPhotoAnswer).toHaveBeenCalledTimes(2));
    expect(api.uploadPhotoAnswer.mock.calls[0][1]).toBe('q1'); expect(api.uploadPhotoAnswer.mock.calls[0][3]).toBe(api.uploadPhotoAnswer.mock.calls[1][3]);
    expect(screen.getByText('Zdjęcie wysłane')).toBeInTheDocument();
  });

  it('renders controlled invalid-photo feedback and restores an accepted photo after refresh', async () => {
    media.preparePhotoAnswer.mockRejectedValue(new media.GameMediaError('unsupported'));
    const { rerender } = renderGame({ ...baseSnapshot, game: game('CollectingPhotoAnswers') });
    fireEvent.change(screen.getByLabelText('Zrób lub wybierz zdjęcie'), { target: { files: [new File(['x'], 'not-image.txt', { type: 'text/plain' })] } });
    expect(await screen.findByRole('alert')).toHaveTextContent('Wybierz prawidłowy plik graficzny.');
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, game: game('CollectingPhotoAnswers') }} privateState={{ ...privateState, hasSubmittedPhotoAnswer: true }} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);
    expect(screen.getByText('Zdjęcie wysłane')).toBeInTheDocument();
  });

  it('prevents empty drawing submission, submits an inked drawing once, and restores it after refresh', async () => {
    renderGame({ ...baseSnapshot, game: game('CollectingDrawingAnswers') }, { ...privateState, isEligibleForDrawingAnswer: true });
    expect(screen.getByRole('button', { name: 'Wyślij rysunek' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Obszar rysowania' })); media.drawingPng.mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    const submit = screen.getByRole('button', { name: 'Wyślij rysunek' }); fireEvent.click(submit); fireEvent.click(submit);
    await waitFor(() => expect(api.uploadDrawingAnswer).toHaveBeenCalledTimes(1)); expect(screen.getByText('Rysunek wysłany')).toBeInTheDocument();
    renderGame({ ...baseSnapshot, game: game('CollectingDrawingAnswers') }, { ...privateState, hasSubmittedDrawingAnswer: true, isEligibleForDrawingAnswer: true });
    expect(screen.getAllByText('Rysunek wysłany')).toHaveLength(2);
  });

  it('renders media options, contains broken media, and sends one idempotent photo vote', async () => {
    hub.submitPhotoAnswerVote.mockResolvedValue(undefined);
    renderGame({ ...baseSnapshot, game: { ...game('CollectingPhotoAnswerVotes'), photoAnswerResults: { questionInstanceId: 'q1', submittedPlayers: 2, requiredPlayers: 3, anonymousOptions: [{ photoAnswerId: 'photo-1', displayPhotoUrl: '/photo-full', thumbnailPhotoUrl: '/photo-thumb', displayOrder: 0, width: 100, height: 100 }, { photoAnswerId: 'photo-2', displayPhotoUrl: '/broken', thumbnailPhotoUrl: '/broken', displayOrder: 1, width: 100, height: 100 }] } } });
    fireEvent.error(screen.getByAltText('Głosowanie 2')); expect(screen.getByLabelText('Medium jest niedostępne.')).toBeInTheDocument();
    const option = screen.getByRole('button', { name: 'Głosowanie 1' }); fireEvent.click(option); fireEvent.click(option); fireEvent.click(screen.getByRole('button', { name: 'Oddaj głos' }));
    await waitFor(() => expect(hub.submitPhotoAnswerVote).toHaveBeenCalledTimes(1)); expect(hub.submitPhotoAnswerVote).toHaveBeenCalledWith(session, 'photo-1', 'q1', expect.any(String));
  });

  it('renders drawings and sends a drawing vote', async () => {
    hub.submitDrawingAnswerVote.mockResolvedValue(undefined);
    renderGame({ ...baseSnapshot, game: { ...game('CollectingDrawingAnswerVotes'), drawingAnswerResults: { anonymousOptions: [{ drawingAnswerId: 'drawing-1', displayDrawingUrl: '/drawing', thumbnailDrawingUrl: '/drawing-thumb', displayOrder: 0, width: 100, height: 100 }] } } });
    fireEvent.click(screen.getByRole('button', { name: 'Głosowanie 1' })); fireEvent.click(screen.getByRole('button', { name: 'Oddaj głos' }));
    await waitFor(() => expect(hub.submitDrawingAnswerVote).toHaveBeenCalledWith(session, 'drawing-1', 'q1', expect.any(String))); expect(screen.getByText('Głos oddany')).toBeInTheDocument();
  });

  it('restores submitted text and vote state from the private reconnect payload', () => {
    const submitted = { ...privateState, hasSubmittedTextAnswer: true, hasSubmittedTextAnswerVote: true };
    const { rerender } = renderGame({ ...baseSnapshot, game: game('CollectingTextAnswers') }, submitted);
    expect(screen.getByText('Odpowiedź wysłana')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, game: { ...game('CollectingTextAnswerVotes'), textResults: { questionInstanceId: 'q1', answeredPlayers: 3, requiredPlayers: 3, votingOptions: [] } } }} privateState={submitted} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);
    expect(screen.getByText('Głos oddany')).toBeInTheDocument();
  });

  it('renders a countdown without advancing photo or drawing stages locally', () => {
    const { rerender } = renderGame({ ...baseSnapshot, game: game('CollectingPhotoAnswers') });
    expect(screen.getByText(/Pozostały czas/)).toBeInTheDocument(); expect(screen.getByLabelText('Zrób lub wybierz zdjęcie')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, game: game('CollectingDrawingAnswers') }} privateState={{ ...privateState, isEligibleForDrawingAnswer: true }} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />);
    expect(screen.getByRole('button', { name: 'Obszar rysowania' })).toBeInTheDocument();
  });

  it('routes every authoritative result, summary, and completed stage through the full lifecycle', () => {
    const summary = { roundNumber: 1, ranking: [{ playerId: 'p1', score: 500, rank: 1 }, { playerId: 'p2', score: 0, rank: 2 }, { playerId: 'p3', score: 0, rank: 2 }], hasNextRound: true };
    const { rerender } = renderGame({ ...baseSnapshot, game: { ...game('ShowingQuestionResults'), playerSelectionResults: { questionInstanceId: 'q1', answeredPlayers: 3, requiredPlayers: 3, missingPlayers: 0, highestVoteCount: 1, options: [{ selectedPlayerId: 'p2', selectedPlayerNickname: 'Ania', voteCount: 1, isTopResult: true, voters: [] }] } } }); expect(screen.getByText('Wyniki')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, stateVersion: 5, game: { ...game('ShowingTextAnswerResults'), textResults: { questionInstanceId: 'q1', answeredPlayers: 3, requiredPlayers: 3, options: [{ answerId: 'a', text: 'Tekst', authorPlayerId: 'p1', authorPlayerNickname: 'Wojtek', voteCount: 1, isTopResult: true, voters: [] }] } } }} privateState={privateState} locale="pl" status="reconnecting" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />); expect(screen.getByText('Tekst')).toBeInTheDocument(); expect(screen.getByText('Przywracanie połączenia…')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, stateVersion: 6, game: { ...game('ShowingPhotoAnswerResults'), photoAnswerResults: { questionInstanceId: 'q1', submittedPlayers: 1, requiredPlayers: 1, options: [{ photoAnswerId: 'photo', width: 100, height: 100, authorPlayerId: 'p1', authorNickname: 'Wojtek', voteCount: 1, isTopResult: true, voters: [] }] } } }} privateState={privateState} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />); expect(screen.getByRole('heading', { name: /Wyniki zdjęć/ })).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, stateVersion: 7, game: { ...game('ShowingDrawingAnswerResults'), drawingAnswerResults: { options: [{ drawingAnswerId: 'drawing', width: 100, height: 100, authorPlayerId: 'p1', authorNickname: 'Wojtek', voteCount: 1, isTopResult: true, voters: [] }] } } }} privateState={privateState} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />); expect(screen.getByRole('heading', { name: 'Wyniki · Wyniki rysunków' })).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, stateVersion: 8, game: { ...game('RoundSummary'), roundSummary: summary } }} privateState={privateState} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />); expect(screen.getByText('Podsumowanie rundy 1')).toBeInTheDocument();
    rerender(<GameRouter session={session} snapshot={{ ...baseSnapshot, stateVersion: 9, game: { ...game('Completed'), ranking: summary.ranking } }} privateState={privateState} locale="pl" status="connected" t={(key) => translations.pl[key]} onSnapshot={() => undefined} />); expect(screen.getByRole('heading', { name: 'Gra zakończona' })).toBeInTheDocument();
  });

  it('shows every final-round editor the server-provided target caption', () => {
    const finalPrivate: PlayerPrivateGameState = { ...privateState, finalRound: { hasSubmittedSelfie: true, assignedArtifactId: 'artifact-1', sourceDisplayMediaUrl: '/api/media/source', hasSubmittedEdit: false, hasSubmittedVote: false, targetRole: { pl: 'bandyta' }, canSubmitSelfie: false } };
    renderGame({ ...baseSnapshot, game: { ...game('CollectingFinalEdits'), finalRound: { currentPass: 1, totalPasses: 2, submittedSelfies: 3, requiredSelfies: 3, submittedEdits: 0, requiredEdits: 3, submittedVotes: 0, requiredVotes: 3, artifacts: [{ artifactId: 'artifact-1', subjectPlayerId: 'p2', subjectNickname: 'Ania', selfiePrompt: { pl: 'Mina' }, targetRole: { pl: 'bandyta' }, voteCount: 0, isTopResult: false }] } } }, finalPrivate);
    expect(screen.getByRole('heading', { name: 'Spraw, aby Ania wyglądał jak bandyta' })).toBeInTheDocument();
  });

  it.each(['CategoryIntro', 'QuestionIntro', 'RevealingTextAnswers', 'RevealingPhotoAnswers', 'RevealingDrawingAnswers', 'PausedForDisplay', 'GameSummary'])('renders a controlled player-facing view for %s', (stage) => {
    renderGame({ ...baseSnapshot, game: game(stage) });
    expect(screen.queryByText('Unsupported')).not.toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
  });
});
