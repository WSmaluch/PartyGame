import { gameQuestion, type GameSnapshot } from '../../api/types';

/** Stable identity for stage motion. SignalR refreshes and timer ticks are excluded. */
export function motionIdentity(game: GameSnapshot): string {
  const question = gameQuestion(game);
  const questionIdentity = question?.instanceId ?? question?.id ?? 'no-question';

  if (game.stage === 'CollectingFinalEdits') {
    return `${game.stage}:${game.finalRound?.currentPass ?? 'pending'}`;
  }

  if (game.stage === 'ShowingFinalPresentation') {
    return `${game.stage}:${(game.finalRound?.artifacts ?? []).map((artifact) => `${artifact.artifactId}:${artifact.displayMediaUrl ?? 'pending'}`).join('|')}`;
  }

  return `${game.stage}:${questionIdentity}`;
}
