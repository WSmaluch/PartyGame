import { useState } from 'react';
import {
  gameCategory,
  gameQuestion,
  localizedText,
  type GameSnapshot,
  type RoomSnapshot,
} from '../../api/types';
import { t } from '../../translations';
import { profilePhotoUrl, publicMediaUrl } from '../../api/roomApi';
import {
  CollectingTextAnswers,
  RevealingTextAnswers,
  CollectingTextAnswerVotes,
  ShowingTextAnswerResults,
} from './TextAnswerScreens';
import { useTimer } from './useTimer';
import {
  CollectingPhotoAnswersView,
  RevealingPhotoAnswersView,
  CollectingPhotoAnswerVotesView,
  ShowingPhotoAnswerResultsView,
} from './PhotoAnswerScreens';
import {
  CollectingDrawingAnswersView,
  RevealingDrawingAnswersView,
  CollectingDrawingAnswerVotesView,
  ShowingDrawingAnswerResultsView,
} from './DrawingAnswerScreens';

export function GameScreens({ snapshot }: { snapshot: RoomSnapshot }) {
  const game = snapshot.game;
  if (!game || game.stage === 'NotStarted') {
    return (
      <div className="central-message started" aria-live="assertive">
        <div className="party-icon" aria-hidden="true">
          🎉
        </div>
        <h2>Gra rozpoczęta!</h2>
        <p>
          Pokój {snapshot.roomCode}. Pierwsza runda pojawi się w następnym
          etapie.
        </p>
      </div>
    );
  }

  const renderStage = () => {
    switch (game.stage) {
      case 'CategoryIntro':
        return <CategoryIntro game={game} />;
      case 'QuestionIntro':
        return <QuestionIntro game={game} />;
      case 'CollectingPlayerSelections':
        return <CollectingPlayerSelections game={game} room={snapshot} />;
      case 'ShowingQuestionResults':
        return <ShowingQuestionResults game={game} />;
      case 'RoundSummary':
        return <RoundSummary game={game} room={snapshot} />;
      case 'Completed':
        return <Completed game={game} room={snapshot} />;
      case 'PausedForDisplay':
        return <PausedForDisplay />;
      case 'CollectingTextAnswers':
        return <CollectingTextAnswers game={game} room={snapshot} />;
      case 'RevealingTextAnswers':
        return <RevealingTextAnswers game={game} />;
      case 'CollectingTextAnswerVotes':
        return <CollectingTextAnswerVotes game={game} room={snapshot} />;
      case 'ShowingTextAnswerResults':
        return <ShowingTextAnswerResults game={game} />;
      case 'CollectingPhotoAnswers':
        return <CollectingPhotoAnswersView game={game} room={snapshot} />;
      case 'RevealingPhotoAnswers':
        return <RevealingPhotoAnswersView game={game} />;
      case 'CollectingPhotoAnswerVotes':
        return <CollectingPhotoAnswerVotesView game={game} />;
      case 'ShowingPhotoAnswerResults':
        return <ShowingPhotoAnswerResultsView game={game} room={snapshot} />;
      case 'CollectingDrawingAnswers':
        return <CollectingDrawingAnswersView game={game} room={snapshot} />;
      case 'RevealingDrawingAnswers':
        return <RevealingDrawingAnswersView game={game} />;
      case 'CollectingDrawingAnswerVotes':
        return <CollectingDrawingAnswerVotesView game={game} />;
      case 'ShowingDrawingAnswerResults':
        return <ShowingDrawingAnswerResultsView game={game} />;
      case 'CollectingFinalSelfies':
      case 'CollectingFinalEdits':
        return <FinalRoundProgress game={game} />;
      case 'ShowingFinalPresentation':
      case 'CollectingFinalVotes':
      case 'ShowingFinalResults':
        return <FinalRoundPresentation game={game} />;
      default:
        // Handle unknown or unhandled game stages safely
        return <UnknownStage />;
    }
  };

  return <div className="game-screen-container">{renderStage()}</div>;
}

function FinalRoundProgress({ game }: { game: GameSnapshot }) {
  const final = game.finalRound;
  const editing = game.stage === 'CollectingFinalEdits';
  return <div className="central-message" aria-live="polite" data-testid={editing ? 'final-round-edits-collecting' : 'final-round-selfies-collecting'}><h2>Runda finałowa</h2><p>{editing ? `Edycja ${final?.currentPass ?? 0}/${final?.totalPasses ?? 0}` : 'Zdjęcia finałowe'}</p><strong>{editing ? `${final?.submittedEdits ?? 0}/${final?.requiredEdits ?? 0}` : `${final?.submittedSelfies ?? 0}/${final?.requiredSelfies ?? 0}`}</strong></div>;
}

function FinalRoundPresentation({ game }: { game: GameSnapshot }) {
  const stage = game.stage === 'ShowingFinalPresentation' ? 'presentation' : game.stage === 'CollectingFinalVotes' ? 'voting' : 'results';
  return <section className="photo-results" aria-label="Final round presentation" data-testid={`final-round-${stage}`}><h2>Runda finałowa</h2><div className="photo-grid">{(game.finalRound?.artifacts ?? []).map((artifact) => <article key={artifact.artifactId} className="photo-card">{artifact.displayMediaUrl ? <img src={publicMediaUrl(artifact.displayMediaUrl)} alt={`${artifact.subjectNickname} as ${localizedText(artifact.targetRole)}`} /> : <div className="photo-placeholder">Przygotowywanie zdjęcia…</div>}<h3>{artifact.subjectNickname} as {localizedText(artifact.targetRole)}</h3>{game.stage === 'ShowingFinalResults' && <p>{artifact.voteCount} głosów{artifact.isTopResult ? ' 🏆' : ''}</p>}</article>)}</div></section>;
}

function CategoryIntro({ game }: { game: GameSnapshot }) {
  const category = gameCategory(game);
  return (
    <div className="category-intro" aria-live="assertive">
      <h2>{t('category')}</h2>
      <h1>{localizedText(category?.name)}</h1>
      {category?.description && <p>{localizedText(category.description)}</p>}
    </div>
  );
}

function QuestionIntro({ game }: { game: GameSnapshot }) {
  return (
    <div className="question-intro" aria-live="assertive">
      <h2>
        {t('question')} {game.currentQuestionNumber} /{' '}
        {game.questionsInCurrentRound}
      </h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>
    </div>
  );
}

function CollectingPlayerSelections({
  game,
  room,
}: {
  game: GameSnapshot;
  room: RoomSnapshot;
}) {
  const timeLeft = useTimer(game.stageEndsAtUtc);
  const maxPlayers = game.requiredPlayers || room.players.length;
  // We mock the answered status by taking the first `answeredCount` players as answered
  // But wait! We no longer have `answeredPlayerIds` during this stage in `GameSnapshot`.
  // Wait, in `types.ts` I added `answeredPlayers` and `requiredPlayers`.
  // The Prompt for Phase 2B says: "In CollectingPlayerSelections, only show the count/avatars of answered players. Do NOT show their votes or points yet!"
  // The user said: "only show the count/avatars of answered players"
  // But without `answeredPlayerIds`, we can't show which avatars specifically, just the count or a mock. Let me check the Phase 3B instruction again. The prompt says "Do not request PlayerPrivateGameState". Actually the Phase 2B rule is "only show the count/avatars of answered players. Do NOT show their votes or points yet!". If we just use the count, we don't know who exactly. Wait, `game.answeredPlayerIds` is available? It was removed from model in "CollectingPlayerSelections" for results, wait, "W czasie trwania `CollectingPlayerSelections`, model **nie zawiera** obiektu `Results`, chroniąc informacje przed wyciekiem...". It says it DOES NOT contain `Results`. But maybe `AnsweredPlayerIds` is available? Yes, `game.answeredPlayerIds` is an optional array of Guids.
  const answered = game.answeredPlayerIds || [];

  return (
    <div className="collecting-selections" aria-live="polite">
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>

      <div className="timer-container">
        <span className="timer">{timeLeft}s</span>
      </div>

      <div className="answered-status">
        <p>{t('waitingForVotes')}</p>
        <p>
          {t('answered')}: {answered.length} {t('outOf')} {maxPlayers}{' '}
          {t('players')}
        </p>
      </div>

      <div className="answered-players" data-testid="selection-player-grid">
        {room.players.map((p) => {
          const hasAnswered = answered.includes(p.id);
          const photo = profilePhotoUrl(p.profilePhotoUrl);
          return (
            <article key={p.id} data-testid={`selection-player-${p.id}`} className={`player-avatar ${hasAnswered ? 'answered' : 'waiting'}`}>
              {photo ? <img src={`${photo}${photo.includes('?') ? '&' : '?'}v=${room.stateVersion}`} alt={p.nickname} /> : <div className="photo-placeholder">?</div>}
              <span>{p.nickname}</span>
            </article>
          );
        })}
      </div>
    </div>
  );
}

function ShowingQuestionResults({ game }: { game: GameSnapshot }) {
  // Respect prefers-reduced-motion
  const prefersReducedMotion = window.matchMedia(
    '(prefers-reduced-motion: reduce)',
  ).matches;

  const [now] = useState(() => Date.now());
  const timeLeftMs = game.stageEndsAtUtc
    ? new Date(game.stageEndsAtUtc).getTime() - now
    : 10000;
  const delayFactor = timeLeftMs < 5000 ? 0.2 : 0.5;

  const options = (game.results ?? game.playerSelectionResults)?.options || [];

  return (
    <div
      className={`showing-results ${prefersReducedMotion ? 'reduced-motion' : ''}`}
      aria-live="assertive"
    >
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>

      <div className="results-grid">
        {options.map((opt, i) => {
          return (
            <div
              key={opt.selectedPlayerId}
              className={`result-option ${opt.isTopResult ? 'top-result' : ''}`}
              style={{
                animationDelay: prefersReducedMotion
                  ? '0s'
                  : `${i * delayFactor}s`,
              }}
            >
              <h3>{opt.selectedPlayerNickname}</h3>
              <div className="voters">
                {opt.voters.map((voter) => {
                  return (
                    <div key={voter.playerId} className="voter-badge">
                      <span>{voter.nickname}</span>
                      <span className="points">
                        +{voter.pointsAwarded} {t('points')}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function RoundSummary({
  game,
  room,
}: {
  game: GameSnapshot;
  room: RoomSnapshot;
}) {
  const rankings =
    game.ranking ??
    game.roundSummary?.ranking ??
    game.roundSummary?.rankings ??
    [];

  return (
    <div className="round-summary" aria-live="assertive">
      <h2>
        {t('roundSummary')} - Round {game.roundSummary?.roundNumber}
      </h2>
      <div className="rankings">
        {[...rankings].sort((a, b) => (a.rank ?? 999) - (b.rank ?? 999) || b.score - a.score).map((rank) => {
          const player = room.players.find((p) => p.id === rank.playerId);
          return (
            <div key={rank.playerId} data-testid={`ranking-entry-${rank.playerId}`} className="ranking-entry">
              <span className="rank">#{rank.rank}</span>
              <span className="name">{player?.nickname}</span>
              <span className="score">
                {rank.score} {t('points')}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function Completed({ game, room }: { game: GameSnapshot; room: RoomSnapshot }) {
  const rankings =
    game.ranking ??
    game.roundSummary?.ranking ??
    game.roundSummary?.rankings ??
    [];
  return (
    <div className="game-completed" aria-live="assertive">
      <h1>{t('gameCompleted')}</h1>
      <div className="rankings" aria-label="Końcowy ranking">
        {[...rankings].sort((a, b) => (a.rank ?? 999) - (b.rank ?? 999) || b.score - a.score).map((rank) => {
          const player = room.players.find(
            (candidate) => candidate.id === rank.playerId,
          );
          return (
            <div key={rank.playerId} data-testid={`ranking-entry-${rank.playerId}`} className="ranking-entry">
              <span className="rank">#{rank.rank}</span>
              <span className="name">{player?.nickname}</span>
              <span className="score">
                {rank.score} {t('points')}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function PausedForDisplay() {
  return (
    <div className="paused-display" aria-live="assertive">
      <h1>{t('paused')}</h1>
    </div>
  );
}

function UnknownStage() {
  return (
    <div className="unknown-stage" aria-live="polite">
      <h2>Oczekiwanie...</h2>
    </div>
  );
}
