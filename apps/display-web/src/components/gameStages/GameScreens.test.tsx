import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { describe, it, expect, vi } from 'vitest';

import { GameScreens } from './GameScreens';
import type { GameSnapshot, RoomSnapshot, GameStage } from '../../api/types';

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

describe('GameScreens', () => {
    const defaultGame: GameSnapshot = {
        stage: 'CategoryIntro',
        currentRoundNumber: 1,
        totalRounds: 3,
        currentQuestionNumber: 1,
        questionsInCurrentRound: 4,
        scores: [],
        currentCategory: { id: 'c1', name: 'CatName', description: 'CatDesc' },
        currentQuestion: { id: 'q1', text: 'QuestText' }
    };
    const defaultRoom: RoomSnapshot = {
        roomCode: 'ABCD',
        phase: 'Started',
        stateVersion: 1,
        displayConnected: true,
        minimumPlayers: 3,
        maximumPlayers: 10,
        canStart: true,
        settings: {} as RoomSnapshot['settings'],
        players: [],
        createdAtUtc: new Date().toISOString()
    };

    it('CategoryIntro and QuestionIntro', () => {
        const { rerender } = render(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'CategoryIntro' } }} />);
        expect(screen.getByText('CatName')).toBeInTheDocument();
        
        rerender(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'QuestionIntro' } }} />);
        expect(screen.getByText('QuestText')).toBeInTheDocument();
    });

    it('CollectingPlayerSelections does not leak partial results and respects requiredPlayers', () => {
        const game: GameSnapshot = {
            ...defaultGame,
            stage: 'CollectingPlayerSelections',
            answeredPlayers: 2,
            requiredPlayers: 3,
            answeredPlayerIds: ['p1', 'p2']
        };
        render(<GameScreens snapshot={{ ...defaultRoom, players: [{id:'p1', nickname:'A', isHost:false, isReady:true, isConnected:true, hasProfilePhoto:false, score:0}, {id:'p2', nickname:'B', isHost:false, isReady:true, isConnected:true, hasProfilePhoto:false, score:0}, {id:'p3', nickname:'C', isHost:false, isReady:true, isConnected:true, hasProfilePhoto:false, score:0}], game }} />);
        expect(screen.getByText(/answered/i)).toBeInTheDocument();
        expect(screen.getByText(/2 outOf 3 players/i)).toBeInTheDocument();
    });

    it('ShowingQuestionResults with pointsAwarded', () => {
        const game: GameSnapshot = {
            ...defaultGame,
            stage: 'ShowingQuestionResults',
            playerSelectionResults: {
                questionInstanceId: 'q1',
                answeredPlayers: 3,
                requiredPlayers: 3,
                missingPlayers: 0,
                highestVoteCount: 2,
                options: [
                    { selectedPlayerId: 'a', selectedPlayerNickname: 'A', selectedPlayerPhotoUrl: null, voteCount: 2, isTopResult: true, voters: [ { playerId: 'b', nickname: 'B', pointsAwarded: 200 } ] }
                ]
            }
        };
        render(<GameScreens snapshot={{ ...defaultRoom, game }} />);
        expect(screen.getByText('A')).toBeInTheDocument();
        expect(screen.getByText('B')).toBeInTheDocument();
        expect(screen.getByText('+200 points')).toBeInTheDocument();
    });

    it('RoundSummary and Completed', () => {
        const { rerender } = render(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'RoundSummary', roundSummary: { roundNumber: 1, rankings: [] } } }} />);
        expect(screen.getByText(/roundSummary/i)).toBeInTheDocument();
        
        rerender(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'Completed' } }} />);
        expect(screen.getByText(/gameCompleted/i)).toBeInTheDocument();
    });

    it('PausedForDisplay and unknown GameStage', () => {
        const { rerender } = render(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'PausedForDisplay' } }} />);
        expect(screen.getByText(/paused/i)).toBeInTheDocument();
        
        rerender(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'FutureUnknown' as GameStage } }} />);
        expect(screen.getByText(/Oczekiwanie/i)).toBeInTheDocument();
    });

    it('routes all Stage 5B DrawingAnswer stages safely', () => {
        const expected = ['drawing-collecting', 'revealing-drawing-answers', 'collecting-drawing-answer-votes', 'showing-drawing-answer-results'];
        for (const [index, stage] of ['CollectingDrawingAnswers', 'RevealingDrawingAnswers', 'CollectingDrawingAnswerVotes', 'ShowingDrawingAnswerResults'].entries()) {
            const { unmount } = render(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage } }} />);
            expect(screen.getByTestId(expected[index])).toBeInTheDocument();
            unmount();
        }
    });
    
    it('skrócenie animacji prefers-reduced-motion', () => {
        window.matchMedia = vi.fn().mockImplementation(query => ({
            matches: query === '(prefers-reduced-motion: reduce)',
            media: query,
            onchange: null,
            addListener: vi.fn(),
            removeListener: vi.fn(),
        }));
        render(<GameScreens snapshot={{ ...defaultRoom, game: { ...defaultGame, stage: 'ShowingQuestionResults' } }} />);
        expect(screen.getByText('question')).toBeInTheDocument();
    });
});
