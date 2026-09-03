import { describe, expect, it } from 'vitest';
import type { GameSnapshot } from '../../api/types';
import { motionIdentity } from './motionIdentity';

const base: GameSnapshot = {
  stage: 'QuestionIntro', currentRoundNumber: 1, totalRounds: 1,
  currentQuestionNumber: 1, questionsInCurrentRound: 4, scores: [],
  question: { id: 'question', instanceId: 'instance-1', text: 'Question?' },
};

describe('display motionIdentity', () => {
  it('ignores timer refreshes', () => {
    expect(motionIdentity({ ...base, stageEndsAtUtc: '2026-09-03T10:00:00Z' }))
      .toBe(motionIdentity({ ...base, stageEndsAtUtc: '2026-09-03T10:00:01Z' }));
  });

  it('changes with question, stage, and final artifact', () => {
    expect(motionIdentity(base)).not.toBe(motionIdentity({ ...base, stage: 'CollectingPlayerSelections' }));
    expect(motionIdentity(base)).not.toBe(motionIdentity({ ...base, question: { ...base.question!, instanceId: 'instance-2' } }));
  });
});
