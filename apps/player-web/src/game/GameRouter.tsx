import { useEffect, useMemo, useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import type { ConnectionStatus } from '../realtime/gameHubConnection';
import { gameHubConnection } from '../realtime/gameHubConnection';
import { gameQuestion, localizedText, type GameSnapshot, type PlayerPrivateGameState, type PlayerSession, type PublicPlayer, type RoomSnapshot } from '../api/types';
import { submissionIdentity } from './submissionIdentity';
import type { Locale, TranslationKey } from '../translations';
import { uploadDrawingAnswer, uploadPhotoAnswer } from '../api/playerApi';
import { drawingPng, GameMediaError, preparePhotoAnswer } from '../media/gameMedia';
import { DrawingCanvas } from './DrawingCanvas';
import { CompletedStage, ResultsStage, RoundSummaryStage } from './GameResults';

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
  if ((game.stage === 'CollectingTextAnswers' || game.stage === 'CollectingTextAnswerVotes' || game.stage === 'CollectingPhotoAnswers' || game.stage === 'CollectingPhotoAnswerVotes' || game.stage === 'CollectingDrawingAnswers' || game.stage === 'CollectingDrawingAnswerVotes') && privateState?.questionInstanceId !== questionId) return <GameShell game={game} status={status} t={t}><p>{t('gameLoading')}</p></GameShell>;
  if (game.stage === 'CollectingTextAnswers') return <TextAnswer key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingTextAnswerVotes') return <TextVoting key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingPhotoAnswers') return <PhotoAnswer key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingDrawingAnswers') return <DrawingAnswer key={questionId} session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingPhotoAnswerVotes') return <MediaVoting key={questionId} kind="photo" session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'CollectingDrawingAnswerVotes') return <MediaVoting key={questionId} kind="drawing" session={session} game={game} privateState={privateState} locale={locale} status={status} t={t} onSnapshot={onSnapshot} />;
  if (game.stage === 'ShowingQuestionResults') return <GameShell game={game} status={status} t={t}><ResultsStage kind="selection" game={game} locale={locale} t={t} /></GameShell>;
  if (game.stage === 'ShowingTextAnswerResults') return <GameShell game={game} status={status} t={t}><ResultsStage kind="text" game={game} locale={locale} t={t} /></GameShell>;
  if (game.stage === 'ShowingPhotoAnswerResults') return <GameShell game={game} status={status} t={t}><ResultsStage kind="photo" game={game} locale={locale} t={t} /></GameShell>;
  if (game.stage === 'ShowingDrawingAnswerResults') return <GameShell game={game} status={status} t={t}><ResultsStage kind="drawing" game={game} locale={locale} t={t} /></GameShell>;
  if (game.stage === 'RoundSummary') return <GameShell game={game} status={status} t={t}><RoundSummaryStage game={game} room={snapshot} t={t} /></GameShell>;
  if (game.stage === 'GameSummary') return <GameShell game={game} status={status} t={t}><CompletedStage game={game} room={snapshot} t={t} /></GameShell>;
  if (game.stage === 'Completed') return <GameShell game={game} status={status} t={t}><CompletedStage game={game} room={snapshot} t={t} /></GameShell>;
  if (game.stage === 'CategoryIntro' || game.stage === 'QuestionIntro' || game.stage === 'RevealingTextAnswers' || game.stage === 'RevealingPhotoAnswers' || game.stage === 'RevealingDrawingAnswers' || game.stage === 'PausedForDisplay') return <GameShell game={game} status={status} t={t}><h1>{t('waitingForOthers')}</h1><p>{t('stageWaiting')}</p></GameShell>;
  if (game.stage.includes('Photo') || game.stage.includes('Drawing')) return <GameShell game={game} status={status} t={t}><h1>{t('stageWaiting')}</h1></GameShell>;
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
  return game.currentQuestionNumber > 0 ? <p className="eyebrow">{t('question')} {game.currentQuestionNumber}/{game.questionsInCurrentRound}</p> : null;
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

function PhotoAnswer({ session, game, privateState, locale, status, t, onSnapshot }: { session: PlayerSession; game: GameSnapshot; privateState?: PlayerPrivateGameState; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game); const questionId = question?.instanceId ?? question?.id;
  const authoritativeSubmitted = privateState?.hasSubmittedPhotoAnswer === true;
  const [file, setFile] = useState<Blob>(); const [preview, setPreview] = useState<string>(); const [failure, setFailure] = useState<TranslationKey>(); const [state, setState] = useState<'idle' | 'processing' | 'uploading' | 'submitted' | 'failed'>(authoritativeSubmitted ? 'submitted' : 'idle'); const inFlight = useRef(false);
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);
  async function select(event: ChangeEvent<HTMLInputElement>): Promise<void> { const selected = event.target.files?.[0]; event.target.value = ''; if (!selected) return; setFailure(undefined); setState('processing'); try { const prepared = await preparePhotoAnswer(selected); if (preview) URL.revokeObjectURL(preview); setFile(prepared); setPreview(URL.createObjectURL(prepared)); setState('idle'); } catch (error) { setFailure(photoErrorMessage(error)); setState('failed'); } }
  async function submit(): Promise<void> { if (!file || !questionId || inFlight.current) return; inFlight.current = true; setFailure(undefined); setState('uploading'); try { const result = await uploadPhotoAnswer(session, questionId, file, submissionIdentity(session.roomCode, session.playerId, questionId, 'photo-answer')); onSnapshot(result.roomSnapshot); setState('submitted'); } catch { setFailure('photoUploadFailed'); setState('failed'); } finally { inFlight.current = false; } }
  return <GameShell game={game} status={status} t={t}>
    <h2>{localizedText(question?.text, locale)}</h2>
    {state === 'submitted' || authoritativeSubmitted ? <WaitingState label={t('photoSubmitted')} t={t} /> : <>
      <label className="file-button" htmlFor="photo-answer">{t('takePhoto')}</label>
      <input id="photo-answer" className="visually-hidden" type="file" accept="image/*" capture="environment" onChange={select} />
      {state === 'processing' && <p role="status">{t('processingPhoto')}</p>}
      {preview && <>
        <img className="media-preview" src={preview} alt={t('photoPreview')} />
        <button type="button" className="secondary-action" onClick={() => { setFile(undefined); URL.revokeObjectURL(preview); setPreview(undefined); }}>{t('changePhoto')}</button>
        <button type="button" onClick={() => void submit()} disabled={state === 'uploading' || status !== 'connected'}>{state === 'uploading' ? t('uploadingPhoto') : t('submitPhoto')}</button>
      </>}
      {failure && <Retry onRetry={() => void submit()} t={t} message={t(failure)} />}
    </>}
  </GameShell>;
}

function DrawingAnswer({ session, game, privateState, locale, status, t, onSnapshot }: { session: PlayerSession; game: GameSnapshot; privateState?: PlayerPrivateGameState; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game); const questionId = question?.instanceId ?? question?.id; const authoritativeSubmitted = privateState?.hasSubmittedDrawingAnswer === true; const eligible = privateState?.isEligibleForDrawingAnswer !== false;
  const [canvas, setCanvas] = useState<HTMLCanvasElement>(); const [hasInk, setHasInk] = useState(false); const [state, setState] = useState<SubmissionState>(authoritativeSubmitted ? 'submitted' : 'idle'); const inFlight = useRef(false);
  async function submit(): Promise<void> { if (!canvas || !questionId || !hasInk || inFlight.current) return; inFlight.current = true; setState('submitting'); try { const png = await drawingPng(canvas, hasInk); const result = await uploadDrawingAnswer(session, questionId, png, submissionIdentity(session.roomCode, session.playerId, questionId, 'drawing-answer')); onSnapshot(result.roomSnapshot); setState('submitted'); } catch { setState('failed'); } finally { inFlight.current = false; } }
  return <GameShell game={game} status={status} t={t}><h2>{localizedText(question?.text, locale)}</h2>{state === 'submitted' || authoritativeSubmitted || !eligible ? <WaitingState label={state === 'submitted' || authoritativeSubmitted ? t('drawingSubmitted') : t('waitingForOthers')} t={t} /> : <><DrawingCanvas disabled={state === 'submitting'} onCanvas={setCanvas} onInkChange={setHasInk} labels={{ canvas: t('drawingCanvas'), undo: t('undoDrawing'), clear: t('clearDrawing'), clearConfirm: t('clearDrawingConfirm'), cancel: t('cancel') }} /><button type="button" onClick={() => void submit()} disabled={!hasInk || state === 'submitting' || status !== 'connected'}>{state === 'submitting' ? t('submitting') : t('submitDrawing')}</button>{state === 'failed' && <Retry onRetry={() => void submit()} t={t} message={t('drawingSubmitFailed')} />}</>}</GameShell>;
}

function MediaVoting({ kind, session, game, privateState, locale, status, t, onSnapshot }: { kind: 'photo' | 'drawing'; session: PlayerSession; game: GameSnapshot; privateState?: PlayerPrivateGameState; locale: Locale; status: ConnectionStatus; t: Translate; onSnapshot: (snapshot: RoomSnapshot) => void }) {
  const question = gameQuestion(game); const questionId = question?.instanceId ?? question?.id; const submitted = kind === 'photo' ? privateState?.hasSubmittedPhotoAnswerVote === true : privateState?.hasSubmittedDrawingAnswerVote === true;
  const options = kind === 'photo' ? (game.photoAnswerResults?.anonymousOptions ?? []).map((option) => ({ id: option.photoAnswerId, url: option.thumbnailPhotoUrl || option.displayPhotoUrl, label: `${t('vote')} ${option.displayOrder + 1}` })) : (game.drawingAnswerResults?.anonymousOptions ?? []).map((option, index) => ({ id: option.drawingAnswerId, url: option.thumbnailDrawingUrl ?? option.displayDrawingUrl, label: `${t('vote')} ${(option.displayOrder ?? index) + 1}` }));
  const [selected, setSelected] = useState<string>(); const [state, setState] = useState<SubmissionState>(submitted ? 'submitted' : 'idle'); const inFlight = useRef(false);
  async function submit(): Promise<void> { if (!selected || !questionId || inFlight.current) return; inFlight.current = true; setState('submitting'); try { if (kind === 'photo') await gameHubConnection.submitPhotoAnswerVote(session, selected, questionId, submissionIdentity(session.roomCode, session.playerId, questionId, 'photo-vote')); else await gameHubConnection.submitDrawingAnswerVote(session, selected, questionId, submissionIdentity(session.roomCode, session.playerId, questionId, 'drawing-vote')); onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode)); setState('submitted'); } catch { setState('failed'); } finally { inFlight.current = false; } }
  return <GameShell game={game} status={status} t={t}><h2>{localizedText(question?.text, locale)}</h2>{state === 'submitted' || submitted ? <WaitingState label={t('voteSubmitted')} t={t} /> : <fieldset><legend>{t('vote')}</legend><div className="media-options">{options.map((option) => <button type="button" className={`media-option ${selected === option.id ? 'is-selected' : ''}`} key={option.id} aria-pressed={selected === option.id} aria-label={option.label} onClick={() => setSelected(option.id)}><RemoteMedia src={option.url} alt={option.label} unavailable={t('mediaUnavailable')} loading={t('mediaLoading')} /></button>)}</div><button type="button" onClick={() => void submit()} disabled={!selected || state === 'submitting' || status !== 'connected'}>{state === 'submitting' ? t('submitting') : t('submitVote')}</button>{state === 'failed' && <Retry onRetry={() => void submit()} t={t} message={t('voteFailed')} />}</fieldset>}</GameShell>;
}

function RemoteMedia({ src, alt, unavailable, loading }: { src?: string | null; alt: string; unavailable: string; loading: string }) { const [failedSource, setFailedSource] = useState<string>(); const [loadedSource, setLoadedSource] = useState<string>(); if (!src || failedSource === src) return <span className="media-unavailable" role="img" aria-label={unavailable}>⚠</span>; return <span className="remote-media">{loadedSource !== src && <span className="media-loading" role="status">{loading}</span>}<img className="media-option-image" src={src} alt={alt} onLoad={() => setLoadedSource(src)} onError={() => setFailedSource(src)} /></span>; }

function photoErrorMessage(error: unknown): TranslationKey { if (!(error instanceof GameMediaError)) return 'photoProcessing'; if (error.kind === 'unsupported') return 'photoInvalid'; if (error.kind === 'too-large') return 'photoTooLarge'; return 'photoProcessing'; }

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
