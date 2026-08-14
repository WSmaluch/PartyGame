import { useEffect, useMemo, useState } from 'react';
import { publicMediaUrl } from '../../api/roomApi';
import { gameCategory, gameQuestion, localizedText, type AnonymousPhotoAnswer, type GameSnapshot, type PhotoAnswerResultOption, type RoomSnapshot } from '../../api/types';
import { useTimer } from './useTimer';

function mediaUrl(path: string): string {
  return publicMediaUrl(path) ?? path;
}

export function SafePhoto({ src, alt, width, height, preload = false }: {
  src: string; alt: string; width: number; height: number; preload?: boolean;
}) {
  const [failedSrc, setFailedSrc] = useState<string>();
  const failed = failedSrc === src;
  useEffect(() => {
    if (!preload) return;
    const image = new Image();
    image.src = mediaUrl(src);
    return () => { image.src = ''; };
  }, [preload, src]);

  if (failed) return <div className="photo-fallback" role="img" aria-label={`${alt}. Zdjęcie niedostępne`}>🖼️</div>;
  return <img className="photo-answer-image" src={mediaUrl(src)} alt={alt} width={width} height={height}
    loading={preload ? 'eager' : 'lazy'} onError={() => setFailedSrc(src)} />;
}

function PlayerAvatar({ src, name }: { src?: string | null; name: string }) {
  const [failed, setFailed] = useState(false);
  if (!src || failed) return <span className="photo-player-avatar" role="img" aria-label={name}>{name.slice(0, 1)}</span>;
  return <img className="photo-player-avatar" src={mediaUrl(src)} alt={name} onError={() => setFailed(true)} />;
}

function Header({ game }: { game: GameSnapshot }) {
  const seconds = useTimer(game.stageEndsAtUtc);
  return <header className="photo-stage-header">
    <p>Runda {game.currentRoundNumber} · Pytanie {game.currentQuestionNumber}/{game.questionsInCurrentRound}</p>
    <p className="photo-category">{localizedText(gameCategory(game)?.name)}</p>
    <h1>{localizedText(gameQuestion(game)?.text)}</h1>
    <strong className="timer" aria-label={`Pozostało ${seconds} sekund`}>{seconds}s</strong>
  </header>;
}

export function CollectingPhotoAnswersView({ game, room }: { game: GameSnapshot; room: RoomSnapshot }) {
  const data = game.photoAnswerResults;
  const submitted = new Set(game.answeredPlayerIds ?? []);
  return <section className="photo-stage collecting-photo-answers" data-testid="collecting-photo-answers">
    <Header game={game} />
    <p className="photo-progress">{data?.submittedPlayers ?? 0} z {data?.requiredPlayers ?? room.players.length} graczy przesłało zdjęcie</p>
    <div className="answered-players" aria-label="Gracze, którzy przesłali zdjęcie">
      {room.players.map(player => <div key={player.id} className={`player-avatar ${submitted.has(player.id) ? 'answered' : 'waiting'}`}
        aria-label={`${player.nickname}: ${submitted.has(player.id) ? 'wysłano' : 'oczekuje'}`}>{player.nickname.slice(0, 1)}</div>)}
    </div>
    <p className="photo-waiting">Czekamy na zdjęcia z telefonów…</p>
  </section>;
}

function ordered(options: AnonymousPhotoAnswer[]) { return [...options].sort((a, b) => a.displayOrder - b.displayOrder); }

export function RevealingPhotoAnswersView({ game }: { game: GameSnapshot }) {
  const options = ordered(game.photoAnswerResults?.anonymousOptions ?? []);
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  return <section className={`photo-stage revealing-photo-answers ${reduceMotion ? 'reduced-motion' : ''}`} data-testid="revealing-photo-answers">
    <Header game={game} />
    {options.length === 0 ? <EmptyPhotos /> : <div className="photo-gallery photo-gallery--reveal">
      {options.map((option, index) => <article className="photo-card anonymous" key={option.photoAnswerId}
        style={{ animationDelay: reduceMotion ? '0s' : `${index * 0.25}s` }}>
        <SafePhoto src={option.displayPhotoUrl} alt={`Zdjęcie numer ${index + 1}`} width={option.width} height={option.height}
          preload={index <= 1} />
        <span>Zdjęcie {index + 1}</span>
      </article>)}
    </div>}
  </section>;
}

export function CollectingPhotoAnswerVotesView({ game }: { game: GameSnapshot }) {
  const data = game.photoAnswerResults;
  const options = ordered(data?.anonymousOptions ?? []);
  return <section className="photo-stage collecting-photo-votes" data-testid="collecting-photo-answer-votes">
    <Header game={game} />
    <p className="photo-progress">Zagłosowało {data?.votedPlayers ?? 0} z {data?.requiredVoters ?? 0}</p>
    <p>Głosujcie na telefonach</p>
    <div className="photo-gallery">{options.map((option, index) => <article className="photo-card anonymous" key={option.photoAnswerId}>
      <SafePhoto src={option.thumbnailPhotoUrl} alt={`Zdjęcie numer ${index + 1}`} width={option.width} height={option.height} />
      <span>Zdjęcie {index + 1}</span>
    </article>)}</div>
  </section>;
}

function ResultCard({ option, index }: { option: PhotoAnswerResultOption; index: number }) {
  return <article className={`photo-result-card ${option.isTopResult ? 'top-result' : ''}`}>
    {option.isTopResult && <strong className="winner-badge">Najwięcej głosów</strong>}
    <SafePhoto src={option.displayPhotoUrl} alt={`Zdjęcie ${index + 1}, autor ${option.authorNickname}`} width={option.width} height={option.height} />
    <div className="photo-result-copy">
      <p className="photo-person"><PlayerAvatar src={option.authorPhotoUrl} name={option.authorNickname} /> Autor zdjęcia: <strong>{option.authorNickname}</strong></p>
      <p>{option.voteCount} głosów</p>
      {option.voters.length > 0 && <div><h3>Głosowali</h3>{option.voters.map(voter =>
        <p className="photo-person" key={voter.playerId}><PlayerAvatar src={voter.profilePhotoUrl} name={voter.nickname} />
          <span>{voter.nickname}</span> <strong>+{voter.pointsAwarded} pkt</strong></p>)}</div>}
    </div>
  </article>;
}

export function ShowingPhotoAnswerResultsView({ game, room }: { game: GameSnapshot; room?: RoomSnapshot }) {
  const options = useMemo(() => game.photoAnswerResults?.options ?? [], [game.photoAnswerResults?.options]);
  const names = new Map(room?.players.map(player => [player.id, player.nickname]) ?? []);
  return <section className="photo-stage showing-photo-results" data-testid="showing-photo-answer-results">
    <Header game={game} />
    {options.length === 0 ? <EmptyPhotos /> : <div className="photo-results-grid">
      {options.map((option, index) => <ResultCard key={option.photoAnswerId} option={option} index={index} />)}
    </div>}
    {game.scores.length > 0 && <aside className="photo-ranking" aria-label="Aktualny ranking">
      <h2>Ranking</h2>
      {[...game.scores].sort((a, b) => b.score - a.score).map((entry, index) =>
        <p key={entry.playerId}><strong>{index + 1}. {names.get(entry.playerId) ?? 'Gracz'}</strong><span>{entry.score} pkt</span></p>)}
    </aside>}
  </section>;
}

function EmptyPhotos() { return <div className="photo-empty" role="status"><span aria-hidden="true">📷</span><h2>Nikt nie przesłał zdjęcia</h2></div>; }
