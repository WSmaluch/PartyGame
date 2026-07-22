import { useState, useEffect } from 'react';

export function useTimer(stageEndsAtUtc?: string | null) {
  const [timeLeft, setTimeLeft] = useState(0);

  useEffect(() => {
    if (!stageEndsAtUtc) return;
    const end = new Date(stageEndsAtUtc).getTime();
    
    let frame: number;
    const update = () => {
      const now = Date.now();
      const remaining = Math.max(0, Math.floor((end - now) / 1000));
      setTimeLeft(remaining);
      if (remaining > 0) {
        frame = requestAnimationFrame(update);
      }
    };
    frame = requestAnimationFrame(update);
    return () => cancelAnimationFrame(frame);
  }, [stageEndsAtUtc]);

  return timeLeft;
}
