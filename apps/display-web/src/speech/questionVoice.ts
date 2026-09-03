export type QuestionVoiceUpdate = {
  enabled: boolean;
  roomCode: string;
  stage: string;
  questionId?: string;
  text?: string;
  language: 'pl-PL' | 'en-US';
};

type VoiceDependencies = {
  synthesis?: SpeechSynthesis;
  createUtterance?: (text: string) => SpeechSynthesisUtterance;
  storage?: Storage;
  setDelay?: (callback: () => void, milliseconds: number) => number;
  clearDelay?: (timer: number) => void;
};

const storageKey = 'partygame.display.spokenQuestionIds';

export function questionVoiceSupported(): boolean {
  return typeof window !== 'undefined' && 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window;
}

export class QuestionVoiceController {
  private readonly synthesis?: SpeechSynthesis;
  private readonly createUtterance?: (text: string) => SpeechSynthesisUtterance;
  private readonly storage?: Storage;
  private readonly setDelay: (callback: () => void, milliseconds: number) => number;
  private readonly clearDelay: (timer: number) => void;
  private readonly spoken = new Set<string>();
  private context = '';
  private timer?: number;

  constructor(dependencies: VoiceDependencies = {}) {
    this.synthesis = dependencies.synthesis ?? (questionVoiceSupported() ? window.speechSynthesis : undefined);
    this.createUtterance = dependencies.createUtterance ?? (questionVoiceSupported() ? (text) => new SpeechSynthesisUtterance(text) : undefined);
    this.storage = dependencies.storage ?? (typeof window !== 'undefined' ? window.sessionStorage : undefined);
    this.setDelay = dependencies.setDelay ?? ((callback, milliseconds) => window.setTimeout(callback, milliseconds));
    this.clearDelay = dependencies.clearDelay ?? ((timer) => window.clearTimeout(timer));
    this.loadSpoken();
  }

  update(update: QuestionVoiceUpdate): void {
    const nextContext = `${update.roomCode}:${update.stage}:${update.questionId ?? 'no-question'}`;
    if (nextContext !== this.context) {
      this.cancel();
      this.context = nextContext;
    }

    if (!update.enabled || !update.questionId || !update.text?.trim() || !this.synthesis || !this.createUtterance) return;
    const identity = `${update.roomCode}:${update.questionId}`;
    if (this.spoken.has(identity)) return;

    // Reserve the identity before the delay so equivalent SignalR snapshots
    // cannot enqueue duplicates while the first utterance is pending.
    this.spoken.add(identity);
    this.persistSpoken();
    this.timer = this.setDelay(() => {
      this.timer = undefined;
      try {
        const utterance = this.createUtterance!(update.text!.trim());
        utterance.lang = update.language;
        utterance.rate = 0.97;
        utterance.pitch = 1;
        const languagePrefix = update.language.slice(0, 2).toLowerCase();
        utterance.voice = this.synthesis!.getVoices().find((voice) => voice.lang.toLowerCase() === update.language.toLowerCase())
          ?? this.synthesis!.getVoices().find((voice) => voice.lang.toLowerCase().startsWith(languagePrefix))
          ?? null;
        this.synthesis!.speak(utterance);
      } catch {
        // Speech is an enhancement. Browser policy or unavailable system
        // voices must never interrupt the authoritative game UI.
      }
    }, 320);
  }

  cancel(): void {
    if (this.timer !== undefined) {
      this.clearDelay(this.timer);
      this.timer = undefined;
    }
    try { this.synthesis?.cancel(); } catch { /* optional browser API */ }
  }

  dispose(): void { this.cancel(); }

  private loadSpoken(): void {
    try {
      const stored = JSON.parse(this.storage?.getItem(storageKey) ?? '[]');
      if (Array.isArray(stored)) stored.filter((value): value is string => typeof value === 'string').forEach((value) => this.spoken.add(value));
    } catch { /* Corrupt session preference is harmless. */ }
  }

  private persistSpoken(): void {
    try { this.storage?.setItem(storageKey, JSON.stringify([...this.spoken].slice(-100))); } catch { /* Storage may be unavailable. */ }
  }
}
