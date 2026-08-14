import { fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { GameSnapshot, RoomSnapshot } from '../../api/types';
import { configureApiConfig } from '../../api/apiConfig';
import { GameScreens } from './GameScreens';

const game: GameSnapshot = {
  stage: 'CollectingPhotoAnswers', currentRoundNumber: 1, totalRounds: 1,
  currentQuestionNumber: 2, questionsInCurrentRound: 4, stageEndsAtUtc: new Date(Date.now() + 30_000).toISOString(),
  scores: [], currentQuestion: { id: 'question-a', text: 'Zrób zdjęcie czegoś czerwonego' },
  answeredPlayerIds: ['p1'], photoAnswerResults: {
    questionInstanceId: 'question-a', submittedPlayers: 1, requiredPlayers: 3, votedPlayers: 1, requiredVoters: 3,
    anonymousOptions: [
      { photoAnswerId: 'a1', displayPhotoUrl: '/api/media/m1/display', thumbnailPhotoUrl: '/api/media/m1/thumbnail', displayOrder: 2, width: 1600, height: 900 },
      { photoAnswerId: 'a2', displayPhotoUrl: '/api/media/m2/display', thumbnailPhotoUrl: '/api/media/m2/thumbnail', displayOrder: 1, width: 900, height: 1600 },
    ],
  },
};
const room: RoomSnapshot = {
  roomCode: 'ABCD', phase: 'Started', stateVersion: 20, displayConnected: true, minimumPlayers: 3, maximumPlayers: 8,
  canStart: false, settings: {} as RoomSnapshot['settings'], createdAtUtc: new Date().toISOString(), game,
  players: [
    { id: 'p1', nickname: 'Ola', isHost: true, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
    { id: 'p2', nickname: 'Jan', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
    { id: 'p3', nickname: 'Ewa', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
  ],
};

describe('PhotoAnswer Display', () => {
  beforeEach(() => {
    configureApiConfig({ apiBaseUrl: '/partygame', publicAppUrl: '/partygame/display', buildVersion: 'test' });
    window.matchMedia = vi.fn().mockImplementation(query => ({ matches: false, media: query, onchange: null, addListener: vi.fn(), removeListener: vi.fn() }));
  });

  it('collecting pokazuje wyłącznie postęp i nie tworzy obrazów ani URL-i', () => {
    render(<GameScreens snapshot={room} />);
    expect(screen.getByText('1 z 3 graczy przesłało zdjęcie')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByTestId('collecting-photo-answers')).not.toHaveTextContent('/api/media');
  });

  it('odwzorowuje rzeczywiste pola backendu question i LocalizedText', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, currentQuestion: undefined, question: { id: 'question-a', text: { pl: 'Polskie zadanie', en: 'English task' } } } }} />);
    expect(screen.getByRole('heading', { name: /zadanie|task/i })).toBeInTheDocument();
  });

  it('reveal pokazuje anonimowo zdjęcia w trwałej kolejności', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'RevealingPhotoAnswers' } }} />);
    const photos = screen.getAllByRole('img');
    expect(photos[0]).toHaveAttribute('src', expect.stringContaining('/partygame/api/media/m2/display'));
    expect(screen.queryByText('Ola')).not.toBeInTheDocument();
  });

  it('voting pokazuje miniatury i licznik bez częściowych wyników', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'CollectingPhotoAnswerVotes' } }} />);
    expect(screen.getByText('Zagłosowało 1 z 3')).toBeInTheDocument();
    expect(screen.getAllByRole('img')[0]).toHaveAttribute('src', expect.stringContaining('/thumbnail'));
    expect(screen.queryByText(/Najwięcej/)).not.toBeInTheDocument();
  });

  it('results ujawnia autorów, voterów i pointsAwarded', () => {
    const resultGame: GameSnapshot = { ...game, stage: 'ShowingPhotoAnswerResults', photoAnswerResults: {
      ...game.photoAnswerResults!, anonymousOptions: null, options: [{
        photoAnswerId: 'a1', displayPhotoUrl: '/api/media/m1/display', thumbnailPhotoUrl: '/api/media/m1/thumbnail', width: 1600, height: 900,
        authorPlayerId: 'p1', authorNickname: 'Ola', voteCount: 2, isTopResult: true,
        voters: [{ playerId: 'p2', nickname: 'Jan', pointsAwarded: 200 }],
      }],
    }};
    render(<GameScreens snapshot={{ ...room, game: resultGame }} />);
    expect(screen.getByText(/Autor zdjęcia:/)).toHaveTextContent('Ola');
    expect(screen.getByText('Jan')).toBeInTheDocument();
    expect(screen.getByText('+200 pkt')).toBeInTheDocument();
    expect(screen.getByText('Najwięcej głosów')).toBeInTheDocument();
  });

  it('wyróżnia każdy element remisu wskazany przez backend', () => {
    const option = { photoAnswerId: 'a1', displayPhotoUrl: '/one', thumbnailPhotoUrl: '/thumb', width: 800, height: 800,
      authorPlayerId: 'p1', authorNickname: 'Ola', voteCount: 1, isTopResult: true, voters: [] };
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'ShowingPhotoAnswerResults', photoAnswerResults: {
      ...game.photoAnswerResults!, options: [option, { ...option, photoAnswerId: 'a2', authorNickname: 'Jan' }], anonymousOptions: null,
    } } }} />);
    expect(screen.getAllByText('Najwięcej głosów')).toHaveLength(2);
  });

  it('obsługuje zero zdjęć', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'ShowingPhotoAnswerResults', photoAnswerResults: { ...game.photoAnswerResults!, options: [], anonymousOptions: null } } }} />);
    expect(screen.getByText('Nikt nie przesłał zdjęcia')).toBeInTheDocument();
  });

  it('obsługuje jedno zdjęcie bez pustej sekcji voterów', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'ShowingPhotoAnswerResults', photoAnswerResults: { ...game.photoAnswerResults!, anonymousOptions: null, options: [{
      photoAnswerId: 'a1', displayPhotoUrl: '/one', thumbnailPhotoUrl: '/thumb', width: 800, height: 800,
      authorPlayerId: 'p1', authorNickname: 'Ola', voteCount: 0, isTopResult: false, voters: [],
    }] } } }} />);
    expect(screen.getByText(/Autor zdjęcia:/)).toHaveTextContent('Ola');
    expect(screen.queryByText('Głosowali')).not.toBeInTheDocument();
  });

  it.each([[900, 1600], [1600, 900], [800, 800]])('zachowuje wymiary %ix%i', (width, height) => {
    const local = structuredClone(game);
    local.stage = 'RevealingPhotoAnswers';
    local.photoAnswerResults!.anonymousOptions = [{ photoAnswerId: 'x', displayPhotoUrl: '/x', thumbnailPhotoUrl: '/t', displayOrder: 1, width, height }];
    render(<GameScreens snapshot={{ ...room, game: local }} />);
    expect(screen.getByRole('img')).toHaveAttribute('width', String(width));
    expect(screen.getByRole('img')).toHaveAttribute('height', String(height));
  });

  it('izoluje błąd pojedynczego obrazu', () => {
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'RevealingPhotoAnswers' } }} />);
    fireEvent.error(screen.getAllByRole('img')[0]);
    expect(screen.getByLabelText(/Zdjęcie niedostępne/)).toBeInTheDocument();
    expect(screen.getAllByRole('img')).toHaveLength(2);
  });

  it('respektuje Reduce Motion', () => {
    window.matchMedia = vi.fn().mockReturnValue({ matches: true, addListener: vi.fn(), removeListener: vi.fn() });
    render(<GameScreens snapshot={{ ...room, game: { ...game, stage: 'RevealingPhotoAnswers' } }} />);
    expect(screen.getByTestId('revealing-photo-answers')).toHaveClass('reduced-motion');
  });
});
