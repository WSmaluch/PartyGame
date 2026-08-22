export type SubmissionAction = 'player-selection' | 'text-answer' | 'text-vote' | 'photo-answer' | 'drawing-answer' | 'photo-vote' | 'drawing-vote';

export function submissionIdentity(roomCode: string, playerId: string, questionInstanceId: string, action: SubmissionAction): string {
  const key = `partygame.player.submission.${roomCode}.${playerId}.${questionInstanceId}.${action}`;
  const existing = sessionStorage.getItem(key);
  if (existing) return existing;
  const value = typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : fallbackUuid();
  sessionStorage.setItem(key, value);
  return value;
}

function fallbackUuid(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (character) => {
    const random = Math.floor(Math.random() * 16);
    return (character === 'x' ? random : (random & 3) | 8).toString(16);
  });
}
