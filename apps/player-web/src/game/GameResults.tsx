import { useState } from 'react';
import type { DrawingAnswerResultOption, GameSnapshot, PhotoAnswerResultOption, PlayerSelectionResultOption, RankingEntry, ResultVoter, RoomSnapshot, TextAnswerResultOption } from '../api/types';
import { localizedText } from '../api/types';
import type { Locale, TranslationKey } from '../translations';

type Translate = (key: TranslationKey) => string;

export function ResultsStage({ kind, game, locale, t }: { kind: 'selection' | 'text' | 'photo' | 'drawing'; game: GameSnapshot; locale: Locale; t: Translate }) {
  const question = localizedText((game.question ?? game.currentQuestion)?.text, locale);
  const title = kind === 'selection' ? t('resultsTitle') : `${t('resultsTitle')} · ${kind === 'text' ? t('textResults') : kind === 'photo' ? t('photoResults') : t('drawingResults')}`;
  return <section className="results-stage" aria-labelledby="results-title"><h2 id="results-title">{title}</h2>{question && <p className="result-question">{question}</p>}{kind === 'selection' ? <SelectionResults options={(game.playerSelectionResults ?? game.results)?.options ?? []} t={t} /> : kind === 'text' ? <TextResults options={game.textResults?.options ?? []} t={t} /> : kind === 'photo' ? <PhotoResults options={game.photoAnswerResults?.options ?? []} t={t} /> : <DrawingResults options={game.drawingAnswerResults?.options ?? []} t={t} />}</section>;
}

export function RoundSummaryStage({ game, room, t }: { game: GameSnapshot; room: RoomSnapshot; t: Translate }) {
  const rankings = game.roundSummary?.ranking ?? game.roundSummary?.rankings ?? game.ranking ?? [];
  return <section className="results-stage ranking-stage" aria-labelledby="round-summary-title"><h2 id="round-summary-title">{t('roundSummaryTitle')}{game.roundSummary?.roundNumber ? ` ${game.roundSummary.roundNumber}` : ''}</h2><RankingList rankings={rankings} room={room} t={t} />{game.roundSummary?.hasNextRound && <p className="waiting-next-round" role="status">{t('waitingForNextRound')}</p>}</section>;
}

export function CompletedStage({ game, room, t }: { game: GameSnapshot; room: RoomSnapshot; t: Translate }) {
  const rankings = game.ranking ?? game.roundSummary?.ranking ?? game.roundSummary?.rankings ?? [];
  return <section className="results-stage ranking-stage completed-stage" aria-labelledby="game-completed-title"><h1 id="game-completed-title">{t('gameCompleted')}</h1><h2>{t('finalRanking')}</h2><RankingList rankings={rankings} room={room} t={t} /></section>;
}

export function RankingList({ rankings, room, t }: { rankings: RankingEntry[]; room: RoomSnapshot; t: Translate }) {
  if (!rankings.length) return <p className="result-empty" role="status">{t('rankingUnavailable')}</p>;
  const ordered = rankings.map((entry, index) => ({ entry, index })).sort((a, b) => (a.entry.rank ?? Number.MAX_SAFE_INTEGER) - (b.entry.rank ?? Number.MAX_SAFE_INTEGER) || a.index - b.index);
  return <ol className="ranking-list" aria-label={t('ranking')}>{ordered.map(({ entry }) => <RankingRow key={entry.playerId} entry={entry} room={room} t={t} />)}</ol>;
}

export function RankingRow({ entry, room, t }: { entry: RankingEntry; room: RoomSnapshot; t: Translate }) {
  const player = room.players.find((candidate) => candidate.id === entry.playerId); const nickname = entry.nickname || player?.nickname || t('player'); const avatar = entry.profilePhotoUrl ?? player?.profilePhotoUrl;
  return <li className={`ranking-row ${entry.rank === 1 ? 'ranking-row--winner' : ''}`} aria-label={`${t('place')} ${rankText(entry.rank)}, ${nickname}, ${entry.score} ${t('points')}`}><span className="ranking-place">{rankText(entry.rank)}</span>{avatar ? <img className="avatar" src={avatar} alt={`${t('avatarOf')}: ${nickname}`} onError={(event) => { event.currentTarget.style.display = 'none'; }} /> : <span className="avatar avatar--placeholder" aria-hidden="true">👤</span>}<strong>{nickname}</strong><span className="ranking-score">{entry.score} {t('points')}</span>{entry.rank === 1 && <span className="winner-label">{t('winner')}</span>}</li>;
}

function rankText(rank?: number | null): string { return typeof rank === 'number' && rank > 0 ? `#${rank}` : '—'; }
function Winner({ active, t }: { active: boolean; t: Translate }) { return active ? <strong className="winner-badge" aria-label={t('winner')}>{t('winner')}</strong> : null; }
function Voters({ voters, t }: { voters: ResultVoter[]; t: Translate }) { return voters.length ? <ul className="result-voters" aria-label={t('points')} >{voters.map((voter) => <li key={voter.playerId}>{voter.nickname}<span>+{voter.pointsAwarded} {t('points')}</span></li>)}</ul> : null; }
function Facts({ votes, voters, winner, t }: { votes: number; voters: ResultVoter[]; winner: boolean; t: Translate }) { return <><Winner active={winner} t={t} /><p className="result-votes">{votes} {t('votes')}</p><Voters voters={voters} t={t} /></>; }

function SelectionResults({ options, t }: { options: PlayerSelectionResultOption[]; t: Translate }) { return options.length ? <div className="result-cards">{options.map((option) => <article className={`result-card ${option.isTopResult ? 'result-card--winner' : ''}`} key={option.selectedPlayerId}><Winner active={option.isTopResult} t={t} /><div className="result-person">{option.selectedPlayerPhotoUrl ? <img className="avatar" src={option.selectedPlayerPhotoUrl} alt={`${t('avatarOf')}: ${option.selectedPlayerNickname}`} /> : <span className="avatar avatar--placeholder" aria-hidden="true">👤</span>}<h3>{option.selectedPlayerNickname}</h3></div><Facts votes={option.voteCount} voters={option.voters} winner={false} t={t} /></article>)}</div> : <EmptyResults t={t} />; }
function TextResults({ options, t }: { options: TextAnswerResultOption[]; t: Translate }) { return options.length ? <div className="result-cards">{options.map((option) => <article className={`result-card ${option.isTopResult ? 'result-card--winner' : ''}`} key={option.answerId}><Winner active={option.isTopResult} t={t} /><h3>{option.text}</h3><p className="result-author">{option.authorPlayerNickname}</p><Facts votes={option.voteCount} voters={option.voters} winner={false} t={t} /></article>)}</div> : <EmptyResults t={t} />; }
function PhotoResults({ options, t }: { options: PhotoAnswerResultOption[]; t: Translate }) { return options.length ? <div className="result-cards result-cards--media">{options.map((option, index) => <article className={`result-card ${option.isTopResult ? 'result-card--winner' : ''}`} key={option.photoAnswerId}><Winner active={option.isTopResult} t={t} /><ResultMedia src={option.displayPhotoUrl} alt={`${t('photoResults')} ${index + 1}`} unavailable={t('mediaUnavailable')} /><p className="result-author">{option.authorNickname}</p><Facts votes={option.voteCount} voters={option.voters} winner={false} t={t} /></article>)}</div> : <EmptyResults t={t} />; }
function DrawingResults({ options, t }: { options: DrawingAnswerResultOption[]; t: Translate }) { return options.length ? <div className="result-cards result-cards--media">{options.map((option, index) => <article className={`result-card ${option.isTopResult ? 'result-card--winner' : ''}`} key={option.drawingAnswerId}><Winner active={option.isTopResult} t={t} /><ResultMedia src={option.displayDrawingUrl} alt={`${t('drawingResults')} ${index + 1}`} unavailable={t('mediaUnavailable')} /><p className="result-author">{option.authorNickname}</p><Facts votes={option.voteCount} voters={option.voters} winner={false} t={t} /></article>)}</div> : <EmptyResults t={t} />; }
function ResultMedia({ src, alt, unavailable }: { src?: string | null; alt: string; unavailable: string }) { const [failed, setFailed] = useState(false); if (!src || failed) return <span className="result-media-unavailable" role="img" aria-label={unavailable}>⚠</span>; return <img className="result-media" src={src} alt={alt} onError={() => setFailed(true)} />; }
function EmptyResults({ t }: { t: Translate }) { return <p className="result-empty" role="status">{t('resultsUnavailable')}</p>; }
