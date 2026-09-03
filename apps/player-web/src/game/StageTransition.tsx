import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';

type StageFrame = { identity: string; content: ReactNode };

export function StageTransition({ identity, children }: { identity: string; children: ReactNode }) {
  const latest = useRef<StageFrame>({ identity, content: children });
  const [outgoing, setOutgoing] = useState<StageFrame>();

  useLayoutEffect(() => {
    if (latest.current.identity !== identity) setOutgoing(latest.current);
    latest.current = { identity, content: children };
  }, [children, identity]);

  useEffect(() => {
    if (!outgoing) return;
    const timer = window.setTimeout(() => setOutgoing(undefined), 180);
    return () => window.clearTimeout(timer);
  }, [outgoing]);

  return <div className="stage-swap">
    {outgoing && <div key={`out-${outgoing.identity}`} className="stage-swap__layer stage-swap__layer--out" aria-hidden="true">{outgoing.content}</div>}
    <div key={identity} className="stage-swap__layer stage-swap__layer--in" data-testid="motion-stage" data-motion-key={identity}>{children}</div>
  </div>;
}
