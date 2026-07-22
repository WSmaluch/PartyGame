interface StatusPillProps {
  label: string;
  state: 'good' | 'pending' | 'bad' | 'neutral';
}

export function StatusPill({ label, state }: StatusPillProps) {
  return <span className={`status-pill status-pill--${state}`}>{label}</span>;
}
