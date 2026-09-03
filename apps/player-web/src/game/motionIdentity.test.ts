import { describe, expect, it } from 'vitest';
import type { GameSnapshot } from '../api/types';
import { motionIdentity } from './motionIdentity';

function game(overrides: Partial<GameSnapshot> = {}): GameSnapshot {
  return {
    stage: 'CollectingPlayerSelections', currentRoundNumber: 1, totalRounds: 1,
    currentQuestionNumber: 1, questionsInCurrentRound: 4,
    question: { id: 'question', instanceId: 'instance-1', text: 'Question?' },
    ...overrides,
  };
}

describe('motionIdentity', () => {
  it('does not change for timer-only snapshots', () => {
    expect(motionIdentity(game({ stageEndsAtUtc: '2026-09-03T10:00:00Z' })))
      .toBe(motionIdentity(game({ stageEndsAtUtc: '2026-09-03T10:00:01Z' })));
  });

  it('changes for a new stage or authoritative question instance', () => {
    expect(motionIdentity(game({ stage: 'QuestionIntro' }))).not.toBe(motionIdentity(game()));
    expect(motionIdentity(game())).not.toBe(motionIdentity(game({ question: { id: 'question', instanceId: 'instance-2', text: 'Question?' } })));
  });
});
