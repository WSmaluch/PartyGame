import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { ConnectionStatus } from '../realtime/gameHubConnection';
import { gameHubConnection } from '../realtime/gameHubConnection';
import { gameQuestion, localizedText, type GameSnapshot, type PlayerPrivateGameState, type PlayerSession, type PublicPlayer, type RoomSnapshot } from '../api/types';
import { submissionIdentity } from './submissionIdentity';
import type { Locale, TranslationKey } from '../translations';

type Translate = (key: TranslationKey) => string;
type SubmissionState = 'idle' | 'submitting' | 'submitted' | 'failed';

export function GameRouter({ session, snapshot, privateState, locale, status, t, onSnapshot }: {
  session: PlayerSession;
  snapshot?: RoomSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const game = snapshot?.game;
  if (!game) return <GameShell status={status} t={t}><p>{t('gameLoading')}</p></GameShell>;
  const questionId = gameQuestion(game)?.instanceId ?? gameQuestion(game)?.id ?? game.stage;
  if (game.stage === 'CollectingPlayerSelections') return <PlayerSelection key={questionId} session={session} snapshot={snapshot} game={game} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if ((game.stage === 'CollectingTextAnswers' || game.stage === 'CollectingTextAnswerVotes') && privateState?.questionInstanceId !== questionId) return <GameShell game={game} status={status} t={t}><p>{t('gameLoading')}</p></GameShell>;
  if (game.stage === 'CollectingTextAnswers') return <TextAnswer key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingTextAnswerVotes') return <TextVoting key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage.includes('Photo') || game.stage.includes('Drawing')) return <GameShell game={game} status={status} t={t}><h1>{t('unsupportedQuestionType')}</h1><p>{t('unsupportedQuestionHint')}</p></GameShell>;
  return <GameShell game={game} status={status} t={t}><h1>{t('waitingForOthers')}</h1><p>{t('stageWaiting')}</p></GameShell>;
}

function GameShell({ game, status, t, children }: { game?: GameSnapshot; status: ConnectionStatus; t: Translate; children: ReactNode }) {
  return <main className="page-shell"><section className="card game-card" aria-live="polite">
    {game && <GameHeader game={game} t={t} />}
    <p className="connection" aria-live="polite">{status === 'connected' ? t('connectedStatus') : status === 'reconnecting' ? t('reconnectingHint') : status === 'connecting' ? t('connecting') : t('offline')}</p>
    {children}
    {game && <Countdown key={`${game.stage}.${game.stageEndsAtUtc ?? ''}`} game={game} t={t} />}
  </section></main>;
}

function GameHeader({ game, t }: { game: GameSnapshot; t: Translate }) {
  return <><p className="eyebrow">{t('question')} {game.currentQuestionNumber}/{game.questionsInCurrentRound}</p><h1>{gameQuestion(game) ? undefined : t('gameLoading')}</h1></>;
}

function PlayerSelection({ session, snapshot, game, locale, status, t, onSnapshot }: { session: PlayerSession; snapshot: RoomSnapshot; game: GameSnapshot; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const answered = game.answeredPlayerIds?.includes(session.playerId) ?? false;
  const [selectedId, setSelectedId] = useState<string>();
  const [submission, setSubmission] = useState<SubmissionState>(answered ? 'submitted' : 'idle');
  const inFlight = useRef(false);
  const candidates = snapshot.players.filter((player) => player.id !== session.playerId);
  async function submit(): Promise<void> {
    if (!selectedId || !questionId || inFlight.current) return;
    inFlight.current = true; setSubmission('submitting');
    try {
      await gameHubConnection.submitPlayerSelection(session, selectedId, questionId, submissionIdentity(session.roomCode, session.playerId, questionId, 'player-selection'));
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode)); setSubmission('submitted');
    } catch { setSubmission('failed'); } finally { inFlight.current = false; }
  }
  return <GameShell game={game} status={status} t={t}>
    <h2>{localizedText(question?.text, locale)}</h2>
    {submission === 'submitted' ? <WaitingState label={t('answerSubmitted')} t={t} /> : <fieldset><legend>{t('choosePlayer')}</legend><div className="option-list">{candidates.map((player) => <PlayerOption key={player.id} player={player} selected={selectedId === player.id} stateVersion={snapshot.stateVersion} onSelect={() => setSelectedId(player.id)} />)}</div><button type="button" onClick={() => void submit()} disabled={!selectedId || submission === 'submitting' || status !== 'connected'}>{submission === 'submitting' ? t('submitting') : t('submitAnswer')}</button>{submission === 'failed' && <Retry onRetry={() => void submit()} t={t} message={t('submissionFailed')} />}</fieldset>}
  </GameShell>;
}

function TextAnswer({ session, game, privateState, locale, status, t, onSnapshot }: { session: PlayerSession; game: GameSnapshot; privateState?: PlayerPrivateGameState; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game); const questionId = question?.instanceId ?? question?.id;
  const authoritativeSubmitted = privateState?.questionInstanceId === questionId && privateState?.hasSubmittedTextAnswer === true;
  const [text, setText] = useState(''); const [submission, setSubmission] = useState<SubmissionState>(authoritativeSubmitted ? 'submitted' : 'idle'); const inFlight = useRef(false);
  const count = Array.from(text).length;
  async function submit(): Promise<void> {
    if (!questionId || !text.trim() || count > 150 || inFlight.current) return;
    inFlight.current = true; setSubmission('submitting');
    try {
      await gameHubConnection.submitTextAnswer(session, text.trim(), questionId, submissionIdentity(session.roomCode, session.playerId, questionId, 'text-answer'));
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode)); setSubmission('submitted');
    } catch { setSubmission('failed'); } finally { inFlight.current = false; }
  }
  return <GameShell game={game} status={status} t={t}>
    <h2>{localizedText(question?.text, locale)}</h2>
    {submission === 'submitted' ? <WaitingState label={t('answerSubmitted')} t={t} /> : <><label htmlFor="text-answer">{t('yourAnswer')}</label><textarea id="text-answer" value={text} maxLength={150} onChange={(event) => setText(event.target.value)} /><p className="character-count">{count}/150</p><button type="button" onClick={() => void submit()} disabled={!text.trim() || count > 150 || submission === 'submitting' || status !== 'connected'}>{submission === 'submitting' ? t('submitting') : t('submitAnswer')}</button>{submission === 'failed' && <Retry onRetry={() => void submit()} t={t} message={t('submissionFailed')} />}</>}
  </GameShell>;
}

function TextVoting({ session, game, privateState, locale, status, t, onSnapshot }: { session: PlayerSession; game: GameSnapshot; privateState?: PlayerPrivateGameState; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game); const questionId = question?.instanceId ?? question?.id;
  const authoritativeVoted = privateState?.questionInstanceId === questionId && privateState?.hasSubmittedTextAnswerVote === true;
  const eligible = privateState?.questionInstanceId === questionId && privateState?.isEligibleForTextAnswerVote === true;
  const [selectedId, setSelectedId] = useState<string>(); const [submission, setSubmission] = useState<SubmissionState>(authoritativeVoted ? 'submitted' : 'idle'); const inFlight = useRef(false);
  const options = useMemo(() => game.textResults?.votingOptions ?? [], [game.textResults?.votingOptions]);
  async function submit(): Promise<void> {
    if (!selectedId || !questionId || inFlight.current) return;
    inFlight.current = true; setSubmission('submitting');
    try {
      await gameHubConnection.submitTextAnswerVote(session, selectedId, questionId, submissionIdentity(session.roomCode, session.playerId, questionId, 'text-vote'));
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode)); setSubmission('submitted');
    } catch { setSubmission('failed'); } finally { inFlight.current = false; }
  }
  return <GameShell game={game} status={status} t={t}>
    <h2>{localizedText(question?.text, locale)}</h2>
    {submission === 'submitted' || !eligible ? <WaitingState label={submission === 'submitted' ? t('voteSubmitted') : t('notEligibleToVote')} t={t} /> : <fieldset><legend>{t('vote')}</legend><div className="option-list">{options.map((option) => <label key={option.answerId} className={`text-option ${selectedId === option.answerId ? 'is-selected' : ''}`}><input type="radio" name="text-vote" value={option.answerId} checked={selectedId === option.answerId} onChange={() => setSelectedId(option.answerId)} />{option.text}</label>)}</div><button type="button" onClick={() => void submit()} disabled={!selectedId || submission === 'submitting' || status !== 'connected'}>{submission === 'submitting' ? t('submitting') : t('submitVote')}</button>{submission === 'failed' && <Retry onRetry={() => void submit()} t={t} message={t('voteFailed')} />}</fieldset>}
  </GameShell>;
}

function PlayerOption({ player, selected, stateVersion, onSelect }: { player: PublicPlayer; selected: boolean; stateVersion: number; onSelect: () => void }) {
  const image = player.profilePhotoUrl ? `${player.profilePhotoUrl}${player.profilePhotoUrl.includes('?') ? '&' : '?'}v=${stateVersion}` : undefined;
  return <button type="button" className={`player-option ${selected ? 'is-selected' : ''}`} aria-pressed={selected} onClick={onSelect}>{image ? <img className="avatar" src={image} alt="" /> : <span className="avatar avatar--placeholder" aria-hidden="true">👤</span>}<span>{player.nickname}</span></button>;
}

function WaitingState({ label, t }: { label: string; t: Translate }) { return <div className="game-waiting" role="status"><strong>{label}</strong><p>{t('waitingForOthers')}</p></div>; }
function Retry({ onRetry, t, message }: { onRetry: () => void; t: Translate; message: string }) { return <p className="form-error" role="alert">{message} <button type="button" className="inline-button" onClick={onRetry}>{t('retry')}</button></p>; }

function Countdown({ game, t }: { game: GameSnapshot; t: Translate }) {
  const deadline = game.stageEndsAtUtc ? Date.parse(game.stageEndsAtUtc) : Number.NaN;
  const [now, setNow] = useState(() => gameHubConnection.serverNow());
  useEffect(() => { const timer = window.setInterval(() => setNow(gameHubConnection.serverNow()), 250); return () => window.clearInterval(timer); }, []);
  if (Number.isNaN(deadline)) return null;
  const seconds = Math.max(0, (deadline - now) / 1000);
  return <p className="countdown" aria-live="off">{t('timeRemaining')}: {seconds.toFixed(1)}s{seconds === 0 ? ` — ${t('timerWaiting')}` : ''}</p>;
}
