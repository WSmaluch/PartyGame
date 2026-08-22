import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CompletedStage, RankingList, ResultsStage, RoundSummaryStage } from './GameResults';
import { translations } from '../translations';
import type { GameSnapshot, RoomSnapshot } from '../api/types';

const t = (key: keyof typeof translations.pl) => translations.pl[key];
const room: RoomSnapshot = { roomCode: 'AB12', phase: 'Started', stateVersion: 9, players: [
  { id: 'p1', nickname: 'Wojtek', isHost: true, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
  { id: 'p2', nickname: 'Ania', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
  { id: 'p3', nickname: 'Kamil', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
] };
const game = (stage: string): GameSnapshot => ({ stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1, questionsInCurrentRound: 4, question: { id: 'q', instanceId: 'qi', text: { pl: 'Pytanie testowe' } } });
const rankings = (scores: number[], ranks: Array<number | undefined> = scores.map((_, index) => index + 1)) => scores.map((score, index) => ({ playerId: `p${index + 1}`, score, rank: ranks[index] }));

describe('game results and authoritative ranking', () => {
  it('renders PlayerSelection, Text, Photo and Drawing results from their distinct backend payloads', () => {
    const selection = { ...game('ShowingQuestionResults'), playerSelectionResults: { questionInstanceId: 'qi', answeredPlayers: 3, requiredPlayers: 3, missingPlayers: 0, highestVoteCount: 2, options: [{ selectedPlayerId: 'p2', selectedPlayerNickname: 'Ania', voteCount: 2, isTopResult: true, voters: [{ playerId: 'p1', nickname: 'Wojtek', pointsAwarded: 200 }] }] } };
    const { rerender } = render(<ResultsStage kind="selection" game={selection} locale="pl" t={t} />); expect(screen.getByText('Ania')).toBeInTheDocument(); expect(screen.getByText('2 głosów')).toBeInTheDocument(); expect(screen.getByLabelText('Zwycięzca')).toBeInTheDocument();
    rerender(<ResultsStage kind="text" game={{ ...game('ShowingTextAnswerResults'), textResults: { questionInstanceId: 'qi', answeredPlayers: 3, requiredPlayers: 3, options: [{ answerId: 'a1', text: 'Najlepsza odpowiedź', authorPlayerId: 'p1', authorPlayerNickname: 'Wojtek', voteCount: 3, isTopResult: true, voters: [] }] } }} locale="pl" t={t} />); expect(screen.getByText('Najlepsza odpowiedź')).toBeInTheDocument();
    rerender(<ResultsStage kind="photo" game={{ ...game('ShowingPhotoAnswerResults'), photoAnswerResults: { questionInstanceId: 'qi', submittedPlayers: 3, requiredPlayers: 3, options: [{ photoAnswerId: 'photo', displayPhotoUrl: '/photo.jpg', width: 100, height: 100, authorPlayerId: 'p2', authorNickname: 'Ania', voteCount: 1, isTopResult: true, voters: [] }] } }} locale="pl" t={t} />); expect(screen.getByAltText('Wyniki zdjęć 1')).toBeInTheDocument();
    rerender(<ResultsStage kind="drawing" game={{ ...game('ShowingDrawingAnswerResults'), drawingAnswerResults: { options: [{ drawingAnswerId: 'drawing', displayDrawingUrl: '/drawing.png', width: 100, height: 100, authorPlayerId: 'p3', authorNickname: 'Kamil', voteCount: 1, isTopResult: true, voters: [] }] } }} locale="pl" t={t} />); expect(screen.getByAltText('Wyniki rysunków 1')).toBeInTheDocument();
  });

  it('contains broken result media rather than crashing the result stage', () => {
    render(<ResultsStage kind="photo" game={{ ...game('ShowingPhotoAnswerResults'), photoAnswerResults: { questionInstanceId: 'qi', submittedPlayers: 1, requiredPlayers: 1, options: [{ photoAnswerId: 'photo', displayPhotoUrl: '/missing', width: 100, height: 100, authorPlayerId: 'p1', authorNickname: 'Wojtek', voteCount: 0, isTopResult: false, voters: [] }] } }} locale="pl" t={t} />);
    fireEvent.error(screen.getByAltText('Wyniki zdjęć 1')); expect(screen.getByLabelText('Medium jest niedostępne.')).toBeInTheDocument();
  });

  it.each([
    ['500/0/0', [500, 0, 0], [1, 2, 2]],
    ['100/100/50', [100, 100, 50], [1, 1, 3]],
    ['100/100/100', [100, 100, 100], [1, 1, 1]],
    ['300/200/100', [300, 200, 100], [1, 2, 3]],
  ])('renders backend ranking %s without recalculating places', (_caseName, scores, ranks) => {
    render(<RankingList rankings={rankings(scores, ranks)} room={room} t={t} />); for (const rank of [1, 2, 3]) { const expected = ranks.filter((value) => value === rank).length; expect(screen.queryAllByText(`#${rank}`)).toHaveLength(expected); }
  });

  it('shows a controlled missing rank rather than inventing #1', () => {
    render(<RankingList rankings={rankings([500, 0, 0], [1, undefined, 2])} room={room} t={t} />); expect(screen.getByText('—')).toBeInTheDocument(); expect(screen.getAllByText('#1')).toHaveLength(1);
  });

  it('uses authoritative round and completed rankings consistently, including a 1500-point winner', () => {
    const summary = { ...game('RoundSummary'), roundSummary: { roundNumber: 1, ranking: rankings([500, 0, 0], [1, 2, 2]), hasNextRound: true } }; const { rerender } = render(<RoundSummaryStage game={summary} room={room} t={t} />); expect(screen.getByText('Czekamy na kolejną rundę…')).toBeInTheDocument(); expect(screen.getAllByText('#2')).toHaveLength(2);
    rerender(<CompletedStage game={{ ...game('Completed'), ranking: rankings([1500, 0, 0], [1, 2, 2]) }} room={room} t={t} />); expect(screen.getByRole('heading', { name: 'Gra zakończona' })).toBeInTheDocument(); expect(screen.getByText('1500 pkt')).toBeInTheDocument(); expect(screen.getAllByText('#2')).toHaveLength(2);
  });
});
