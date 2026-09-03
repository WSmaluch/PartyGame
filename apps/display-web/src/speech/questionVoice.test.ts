import { describe, expect, it, vi } from 'vitest';
import { QuestionVoiceController } from './questionVoice';

function harness() {
  const speak = vi.fn();
  const cancel = vi.fn();
  const utterances: Array<{ text: string; lang?: string; rate?: number; pitch?: number; voice?: SpeechSynthesisVoice | null }> = [];
  const synthesis = { speak, cancel, getVoices: () => [{ lang: 'pl-PL', name: 'System PL' }] } as unknown as SpeechSynthesis;
  const controller = new QuestionVoiceController({
    synthesis,
    createUtterance: (text) => { const utterance = { text }; utterances.push(utterance); return utterance as SpeechSynthesisUtterance; },
    storage: { getItem: () => null, setItem: vi.fn() } as unknown as Storage,
    setDelay: (callback) => { callback(); return 1; }, clearDelay: vi.fn(),
  });
  return { controller, speak, cancel, utterances };
}

const first = { enabled: true, roomCode: 'ABCD', stage: 'QuestionIntro', questionId: 'q1', text: 'Kto to zrobił?', language: 'pl-PL' as const };

describe('QuestionVoiceController', () => {
  it('speaks a new question once and ignores equivalent/timer snapshots', () => {
    const { controller, speak } = harness();
    controller.update(first);
    controller.update(first);
    controller.update({ ...first, stage: 'QuestionIntro' });
    expect(speak).toHaveBeenCalledTimes(1);
  });

  it('speaks the next question and cancels on stage/question transitions', () => {
    const { controller, speak, cancel } = harness();
    controller.update(first);
    controller.update({ ...first, stage: 'CollectingPlayerSelections' });
    controller.update({ ...first, stage: 'QuestionIntro', questionId: 'q2', text: 'Następne pytanie' });
    expect(cancel).toHaveBeenCalled();
    expect(speak).toHaveBeenCalledTimes(2);
  });

  it('does nothing when disabled or unsupported', () => {
    const supported = harness();
    supported.controller.update({ ...first, enabled: false });
    expect(supported.speak).not.toHaveBeenCalled();
    expect(() => new QuestionVoiceController({ storage: undefined }).update(first)).not.toThrow();
  });

  it('uses a language-matching system voice and natural rate', () => {
    const { controller, utterances } = harness();
    controller.update(first);
    expect(utterances[0]).toMatchObject({ lang: 'pl-PL', rate: 0.97, pitch: 1, voice: expect.objectContaining({ lang: 'pl-PL' }) });
  });
});
