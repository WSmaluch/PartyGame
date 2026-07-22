import { useState } from 'react';
import { gameQuestion, localizedText, type GameSnapshot, type RoomSnapshot } from '../../api/types';
import { t } from '../../translations';
import { useTimer } from './useTimer';

export function CollectingTextAnswers({ game, room }: { game: GameSnapshot, room: RoomSnapshot }) {
  const timeLeft = useTimer(game.stageEndsAtUtc);
  const answeredCount = game.answeredPlayers || 0;
  const requiredCount = game.requiredPlayers || room.players.length;

  return (
    <div className="collecting-text-answers" aria-live="polite">
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>
      
      <div className="timer-container">
        <span className="timer">{timeLeft}s</span>
      </div>

      <div className="answered-status">
        <p>{t('waitingForAnswers')}</p>
        <p>{t('answered')}: {answeredCount} {t('outOf')} {requiredCount} {t('players')}</p>
      </div>
    </div>
  );
}

export function RevealingTextAnswers({ game }: { game: GameSnapshot }) {
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const [now] = useState(() => Date.now());
  const timeLeftMs = game.stageEndsAtUtc ? new Date(game.stageEndsAtUtc).getTime() - now : 10000;
  const delayFactor = timeLeftMs < 5000 ? 0.2 : 0.5;

  const options = game.textResults?.votingOptions || [];

  return (
    <div className={`revealing-answers ${prefersReducedMotion ? 'reduced-motion' : ''}`} aria-live="assertive">
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>
      
      <div className="answers-grid">
        {options.map((opt, i) => (
          <div key={opt.answerId} className="answer-card" style={{ animationDelay: prefersReducedMotion ? '0s' : `${i * delayFactor}s` }}>
            <p>{opt.text}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

export function CollectingTextAnswerVotes({ game, room }: { game: GameSnapshot, room: RoomSnapshot }) {
  const timeLeft = useTimer(game.stageEndsAtUtc);
  const answeredCount = game.textResults?.answeredPlayers || 0;
  const requiredCount = game.textResults?.requiredPlayers || room.players.length;
  const options = game.textResults?.votingOptions || [];

  return (
    <div className="collecting-text-votes" aria-live="polite">
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>
      
      <div className="timer-container">
        <span className="timer">{timeLeft}s</span>
      </div>

      <div className="answered-status">
        <p>{t('waitingForVotes')}</p>
        <p>{t('voted')}: {answeredCount} {t('outOf')} {requiredCount} {t('players')}</p>
      </div>

      <div className="answers-grid">
        {options.map((opt) => (
          <div key={opt.answerId} className="answer-card static">
            <p>{opt.text}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

export function ShowingTextAnswerResults({ game }: { game: GameSnapshot }) {
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const [now] = useState(() => Date.now());
  const timeLeftMs = game.stageEndsAtUtc ? new Date(game.stageEndsAtUtc).getTime() - now : 10000;
  const delayFactor = timeLeftMs < 5000 ? 0.2 : 0.5;

  const options = game.textResults?.options || [];

  return (
    <div className={`showing-text-results ${prefersReducedMotion ? 'reduced-motion' : ''}`} aria-live="assertive">
      <h2>{t('question')}</h2>
      <h1>{localizedText(gameQuestion(game)?.text)}</h1>
      
      <div className="results-grid">
        {options.map((opt, i) => (
          <div key={opt.answerId} className={`result-option ${opt.isTopResult ? 'top-result' : ''}`} style={{ animationDelay: prefersReducedMotion ? '0s' : `${i * delayFactor}s` }}>
            <div className="answer-content">
              <p className="answer-text">"{opt.text}"</p>
              <div className="author-info">
                <span className="author-name">- {opt.authorPlayerNickname}</span>
              </div>
            </div>
            <div className="voters">
              {opt.voters.map(voter => (
                <div key={voter.playerId} className="voter-badge">
                  <span>{voter.nickname}</span>
                  <span className="points">+{voter.pointsAwarded} {t('points')}</span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
