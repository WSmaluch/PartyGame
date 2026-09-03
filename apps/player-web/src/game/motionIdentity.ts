import { gameQuestion, type GameSnapshot } from '../api/types';

/** Stable identity for visual transitions. Deliberately excludes timers and stateVersion. */
export function motionIdentity(game: GameSnapshot): string {
  const question = gameQuestion(game);
  const questionIdentity = question?.instanceId ?? question?.id ?? 'no-question';

  if (game.stage === 'CollectingFinalEdits') {
    return `${game.stage}:${game.finalRound?.currentPass ?? 'pending'}`;
  }

  if (game.stage === 'ShowingFinalPresentation') {
    const artifacts = (game.finalRound?.artifacts ?? [])
      .map((artifact) => `${artifact.artifactId}:${artifact.displayMediaUrl ?? 'pending'}`)
      .join('|');
    return `${game.stage}:${artifacts}`;
  }

  return `${game.stage}:${questionIdentity}`;
}
