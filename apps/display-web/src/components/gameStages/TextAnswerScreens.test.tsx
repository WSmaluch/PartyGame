import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { describe, it, expect, vi } from 'vitest';
import {
  CollectingTextAnswers,
  RevealingTextAnswers,
  CollectingTextAnswerVotes,
  ShowingTextAnswerResults
} from './TextAnswerScreens';
import type { GameSnapshot, RoomSnapshot } from '../../api/types';

vi.mock('../../translations', () => ({
  t: (key: string) => key
}));

window.matchMedia = vi.fn().mockImplementation(query => ({
  matches: false,
  media: query,
  onchange: null,
  addListener: vi.fn(),
  removeListener: vi.fn(),
}));

describe('TextAnswerScreens', () => {
  const defaultRoom: RoomSnapshot = {
    roomCode: 'TEST',
    phase: 'Started',
    stateVersion: 1,
    displayConnected: true,
    minimumPlayers: 3,
    maximumPlayers: 10,
    canStart: true,
    settings: {} as RoomSnapshot['settings'],
    players: [
      { id: '1', nickname: 'Alice', isHost: true, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
      { id: '2', nickname: 'Bob', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
      { id: '3', nickname: 'Charlie', isHost: false, isReady: true, isConnected: true, hasProfilePhoto: false, score: 0 },
    ],
    createdAtUtc: new Date().toISOString()
  };

  const defaultGame: GameSnapshot = {
    stage: 'CollectingTextAnswers',
    currentRoundNumber: 1,
    totalRounds: 3,
    currentQuestionNumber: 1,
    questionsInCurrentRound: 4,
    scores: [],
    currentQuestion: { id: 'q1', text: 'What is your favorite color?' }
  };

  it('CollectingTextAnswers displays question and waiting status', () => {
    const game = { ...defaultGame, answeredPlayers: 1, requiredPlayers: 3 };
    render(<CollectingTextAnswers game={game} room={defaultRoom} />);
    expect(screen.getByText('What is your favorite color?')).toBeInTheDocument();
    expect(screen.getByText('waitingForAnswers')).toBeInTheDocument();
    expect(screen.getByText(/1 outOf 3 players/i)).toBeInTheDocument();
  });

  it('RevealingTextAnswers displays anonymous answers', () => {
    const game: GameSnapshot = {
      ...defaultGame,
      textResults: {
        questionInstanceId: 'q1',
        answeredPlayers: 3,
        requiredPlayers: 3,
        missingPlayers: 0,
        highestVoteCount: 0,
        votingOptions: [
          { answerId: 'a1', text: 'Red' },
          { answerId: 'a2', text: 'Blue' }
        ]
      }
    };
    render(<RevealingTextAnswers game={game} />);
    expect(screen.getByText('Red')).toBeInTheDocument();
    expect(screen.getByText('Blue')).toBeInTheDocument();
  });

  it('CollectingTextAnswerVotes displays options and voted count', () => {
    const game: GameSnapshot = {
      ...defaultGame,
      textResults: {
        questionInstanceId: 'q1',
        answeredPlayers: 2, // 2 voted
        requiredPlayers: 3,
        missingPlayers: 0,
        highestVoteCount: 0,
        votingOptions: [
          { answerId: 'a1', text: 'Red' },
          { answerId: 'a2', text: 'Blue' }
        ]
      }
    };
    render(<CollectingTextAnswerVotes game={game} room={defaultRoom} />);
    expect(screen.getByText('Red')).toBeInTheDocument();
    expect(screen.getByText('waitingForVotes')).toBeInTheDocument();
    expect(screen.getByText(/2 outOf 3 players/i)).toBeInTheDocument();
  });

  it('ShowingTextAnswerResults displays authors, voters, and pointsAwarded', () => {
    const game: GameSnapshot = {
      ...defaultGame,
      textResults: {
        questionInstanceId: 'q1',
        answeredPlayers: 3,
        requiredPlayers: 3,
        missingPlayers: 0,
        highestVoteCount: 0,
        options: [
          {
            answerId: 'a1',
            text: 'Red',
            authorPlayerId: '1',
            authorPlayerNickname: 'Alice',
            voteCount: 1,
            isTopResult: true,
            voters: [
              { playerId: '2', nickname: 'Bob', pointsAwarded: 100 }
            ]
          }
        ]
      }
    };
    render(<ShowingTextAnswerResults game={game} />);
    expect(screen.getByText(/"Red"/)).toBeInTheDocument();
    expect(screen.getByText('- Alice')).toBeInTheDocument();
    expect(screen.getByText('Bob')).toBeInTheDocument();
    expect(screen.getByText('+100 points')).toBeInTheDocument();
  });
});
