import { useTimer } from './useTimer';

export function StageTimer({ endsAtUtc }: { endsAtUtc?: string | null }) {
  const seconds = useTimer(endsAtUtc);
  const urgency = seconds <= 5 ? 'timer--urgent' : seconds <= 10 ? 'timer--warning' : '';
  return <div className="timer-container"><strong className={`timer ${urgency}`} aria-label={`Pozostało ${seconds} sekund`}>{seconds}s</strong></div>;
}
