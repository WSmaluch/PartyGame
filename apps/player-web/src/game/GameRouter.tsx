import {
  useEffect,
  useRef,
  useState,
  type ChangeEvent,
  type CSSProperties,
  type ReactNode,
} from 'react';
import type { ConnectionStatus } from '../realtime/gameHubConnection';
import { gameHubConnection } from '../realtime/gameHubConnection';
import {
  gameQuestion,
  localizedText,
  type GameSnapshot,
  type PlayerPrivateGameState,
  type PlayerSession,
  type PublicPlayer,
  type RoomSnapshot,
} from '../api/types';
import { submissionIdentity } from './submissionIdentity';
import type { Locale, TranslationKey } from '../translations';
import {
  submitFinalVote,
  uploadDrawingAnswer,
  uploadFinalEdit,
  uploadFinalSelfie,
  uploadPhotoAnswer,
} from '../api/playerApi';
import {
  drawingPng,
  GameMediaError,
  preparePhotoAnswer,
} from '../media/gameMedia';
import { DrawingCanvas } from './DrawingCanvas';
import { CompletedStage, ResultsStage, RoundSummaryStage } from './GameResults';
import { motionIdentity } from './motionIdentity';
import { StageTransition } from './StageTransition';
import { privateActionType, type PrivateActionType } from './privateAction';

type Translate = (key: TranslationKey) => string;
type SubmissionState =
  'idle' | 'preparing' | 'submitting' | 'submitted' | 'failed';

type GameRouterProps = {
  session: PlayerSession;
  snapshot?: RoomSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
  onPrivateState?: (privateState: PlayerPrivateGameState) => void;
  onRecoverFinalEdit?: () => void;
  finalEditRecoveryFailed?: boolean;
  onRecoverPrivateAction?: () => void;
  privateActionRecoveryFailed?: boolean;
};

export function GameRouter(props: GameRouterProps) {
  const game = props.snapshot?.game;
  return (
    <StageTransition identity={game ? motionIdentity(game) : 'loading'}>
      <GameRouteContent {...props} />
    </StageTransition>
  );
}

function GameRouteContent({
  session,
  snapshot,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
  onPrivateState,
  onRecoverFinalEdit,
  finalEditRecoveryFailed = false,
  onRecoverPrivateAction,
  privateActionRecoveryFailed = false,
}: GameRouterProps) {
  const game = snapshot?.game;
  if (!game)
    return (
      <GameShell status={status} t={t}>
        <p>{t('gameLoading')}</p>
      </GameShell>
    );
  const questionId =
    gameQuestion(game)?.instanceId ?? gameQuestion(game)?.id ?? game.stage;
  if (game.stage === 'CollectingPlayerSelections')
    return (
      <PlayerSelection
        key={questionId}
        session={session}
        snapshot={snapshot}
        game={game}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  const actionType = privateActionType(game.stage);
  if (actionType && privateState?.questionInstanceId !== questionId) {
    reportRouterDecision(
      'awaiting-private-action-state',
      game,
      privateState,
      actionType,
    );
    return (
      <PrivateActionUnavailable
        game={game}
        status={status}
        t={t}
        onRecover={onRecoverPrivateAction}
        recoveryFailed={privateActionRecoveryFailed}
      />
    );
  }
  reportRouterDecision(
    actionType ? 'actionable-private-state-matched' : 'public-stage-route',
    game,
    privateState,
    actionType,
  );
  if (game.stage === 'CollectingTextAnswers')
    return (
      <TextAnswer
        key={questionId}
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'CollectingTextAnswerVotes')
    return (
      <TextVoting
        key={questionId}
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'CollectingPhotoAnswers')
    return (
      <PhotoAnswer
        key={questionId}
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'CollectingDrawingAnswers')
    return (
      <DrawingAnswer
        key={questionId}
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'CollectingPhotoAnswerVotes')
    return (
      <MediaVoting
        key={questionId}
        kind="photo"
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'CollectingDrawingAnswerVotes')
    return (
      <MediaVoting
        key={questionId}
        kind="drawing"
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'ShowingQuestionResults')
    return (
      <GameShell game={game} status={status} t={t}>
        <ResultsStage kind="selection" game={game} locale={locale} t={t} />
      </GameShell>
    );
  if (game.stage === 'ShowingTextAnswerResults')
    return (
      <GameShell game={game} status={status} t={t}>
        <ResultsStage kind="text" game={game} locale={locale} t={t} />
      </GameShell>
    );
  if (game.stage === 'ShowingPhotoAnswerResults')
    return (
      <GameShell game={game} status={status} t={t}>
        <ResultsStage kind="photo" game={game} locale={locale} t={t} />
      </GameShell>
    );
  if (game.stage === 'ShowingDrawingAnswerResults')
    return (
      <GameShell game={game} status={status} t={t}>
        <ResultsStage kind="drawing" game={game} locale={locale} t={t} />
      </GameShell>
    );
  if (game.stage === 'RoundSummary')
    return (
      <GameShell game={game} status={status} t={t}>
        <RoundSummaryStage game={game} room={snapshot} t={t} />
      </GameShell>
    );
  if (game.stage === 'CollectingFinalSelfies')
    return (
      <FinalSelfie
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
        onPrivateState={onPrivateState}
      />
    );
  if (game.stage === 'CollectingFinalEdits') {
    // A Final Editing stage contains several independent assignments.  The
    // stage name intentionally does not change between passes, so it is not a
    // safe React identity for canvas/submission state.
    const assignmentKey = finalEditAssignmentKey(game, privateState);
    return (
      <FinalEdit
        key={assignmentKey}
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
        onRecover={onRecoverFinalEdit}
        recoveryFailed={finalEditRecoveryFailed}
      />
    );
  }
  if (game.stage === 'ShowingFinalPresentation')
    return (
      <FinalPresentation game={game} locale={locale} status={status} t={t} />
    );
  if (game.stage === 'CollectingFinalVotes')
    return (
      <FinalVoting
        session={session}
        game={game}
        privateState={privateState}
        locale={locale}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (game.stage === 'ShowingFinalResults')
    return <FinalResults game={game} locale={locale} status={status} t={t} />;
  if (game.stage === 'GameSummary')
    return (
      <GameShell game={game} status={status} t={t}>
        <CompletedStage game={game} room={snapshot} t={t} />
      </GameShell>
    );
  if (game.stage === 'Completed')
    return (
      <Completed
        session={session}
        game={game}
        room={snapshot}
        status={status}
        t={t}
        onSnapshot={onSnapshot}
      />
    );
  if (
    game.stage === 'CategoryIntro' ||
    game.stage === 'QuestionIntro' ||
    game.stage === 'RevealingTextAnswers' ||
    game.stage === 'RevealingPhotoAnswers' ||
    game.stage === 'RevealingDrawingAnswers' ||
    game.stage === 'PausedForDisplay'
  )
    return (
      <GameShell game={game} status={status} t={t}>
        <h1>{t('waitingForOthers')}</h1>
        <p>{t('stageWaiting')}</p>
      </GameShell>
    );
  if (game.stage.includes('Photo') || game.stage.includes('Drawing'))
    return (
      <GameShell game={game} status={status} t={t}>
        <h1>{t('stageWaiting')}</h1>
      </GameShell>
    );
  return (
    <GameShell game={game} status={status} t={t}>
      <h1>{t('waitingForOthers')}</h1>
      <p>{t('stageWaiting')}</p>
    </GameShell>
  );
}

function GameShell({
  game,
  status,
  t,
  children,
  className,
}: {
  game?: GameSnapshot;
  status: ConnectionStatus;
  t: Translate;
  children: ReactNode;
  className?: string;
}) {
  return (
    <main
      className={`page-shell game-page-shell ${className ? 'page-shell--wide-game' : ''}`}
    >
      <section
        className={`card game-card ${className ?? ''}`}
        aria-live="polite"
      >
        <header className="game-header" data-testid="game-header">
          {game ? <GameHeader game={game} t={t} /> : <span />}
          <p className={`connection connection--${status}`} aria-live="polite">
            <span className="connection-dot" aria-hidden="true" />
            {status === 'connected'
              ? t('connectedStatus')
              : status === 'reconnecting'
                ? t('reconnectingHint')
                : status === 'connecting'
                  ? t('connecting')
                  : t('offline')}
          </p>
        </header>
        <div className="stage-content">{children}</div>
        {game && <Countdown game={game} t={t} />}
      </section>
    </main>
  );
}

function PrivateActionUnavailable({
  game,
  status,
  t,
  onRecover,
  recoveryFailed,
}: {
  game: GameSnapshot;
  status: ConnectionStatus;
  t: Translate;
  onRecover?: () => void;
  recoveryFailed: boolean;
}) {
  return (
    <GameShell game={game} status={status} t={t}>
      <p
        role={recoveryFailed ? 'alert' : 'status'}
        data-route-decision={
          recoveryFailed
            ? 'private-action-recovery-exhausted'
            : 'awaiting-private-action-state'
        }
      >
        {recoveryFailed ? t('privateActionUnavailable') : t('gameLoading')}
      </p>
      {onRecover && (
        <button type="button" className="secondary-action" onClick={onRecover}>
          {t('retry')}
        </button>
      )}
    </GameShell>
  );
}

function reportRouterDecision(
  decision: string,
  game: GameSnapshot,
  privateState: PlayerPrivateGameState | undefined,
  questionType: PrivateActionType | undefined,
): void {
  // This deliberately contains only public routing data and boolean action
  // flags. In particular, it must never include a player reconnect token.
  if (
    !import.meta.env.DEV &&
    !new URLSearchParams(window.location.search).has('diagnostics')
  )
    return;
  console.info(
    '[PartyGame player router]',
    JSON.stringify({
      decision,
      publicStage: game.stage,
      questionType: questionType ?? 'public-stage',
      questionInstanceId:
        gameQuestion(game)?.instanceId ?? gameQuestion(game)?.id ?? null,
      privateQuestionInstanceId: privateState?.questionInstanceId ?? null,
      isEligibleForDrawingAnswer:
        privateState?.isEligibleForDrawingAnswer ?? null,
      hasSubmittedDrawingAnswer:
        privateState?.hasSubmittedDrawingAnswer ?? null,
    }),
  );
}

function GameHeader({ game, t }: { game: GameSnapshot; t: Translate }) {
  return game.currentQuestionNumber > 0 ? (
    <p className="eyebrow">
      {t('question')} {game.currentQuestionNumber}/
      {game.questionsInCurrentRound}
    </p>
  ) : null;
}

function PlayerSelection({
  session,
  snapshot,
  game,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  snapshot: RoomSnapshot;
  game: GameSnapshot;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const answered = game.answeredPlayerIds?.includes(session.playerId) ?? false;
  const [selectedId, setSelectedId] = useState<string>();
  const [submission, setSubmission] = useState<SubmissionState>(
    answered ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  const candidates = snapshot.players;
  async function submit(choiceId = selectedId): Promise<void> {
    if (!choiceId || !questionId || inFlight.current) return;
    inFlight.current = true;
    setSubmission('submitting');
    try {
      await gameHubConnection.submitPlayerSelection(
        session,
        choiceId,
        questionId,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          questionId,
          'player-selection',
        ),
      );
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode));
      setSubmission('submitted');
    } catch {
      setSubmission('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(question?.text, locale)}</h2>
      {submission === 'submitted' ? (
        <WaitingState label={t('answerSubmitted')} t={t} />
      ) : (
        <fieldset>
          <legend>{t('choosePlayer')}</legend>
          <div className="option-list">
            {candidates.map((player, index) => (
              <PlayerOption
                key={player.id}
                player={player}
                selected={selectedId === player.id}
                stateVersion={snapshot.stateVersion}
                motionIndex={index}
                onSelect={() => {
                  setSelectedId(player.id);
                  void submit(player.id);
                }}
              />
            ))}
          </div>
          {submission === 'failed' && (
            <Retry
              onRetry={() => void submit()}
              t={t}
              message={t('submissionFailed')}
            />
          )}
        </fieldset>
      )}
    </GameShell>
  );
}

function TextAnswer({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const authoritativeSubmitted =
    privateState?.questionInstanceId === questionId &&
    privateState?.hasSubmittedTextAnswer === true;
  const [text, setText] = useState('');
  const [submission, setSubmission] = useState<SubmissionState>(
    authoritativeSubmitted ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  const count = Array.from(text).length;
  async function submit(): Promise<void> {
    if (!questionId || !text.trim() || count > 150 || inFlight.current) return;
    inFlight.current = true;
    setSubmission('submitting');
    try {
      await gameHubConnection.submitTextAnswer(
        session,
        text.trim(),
        questionId,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          questionId,
          'text-answer',
        ),
      );
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode));
      setSubmission('submitted');
    } catch {
      setSubmission('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(question?.text, locale)}</h2>
      {submission === 'submitted' ? (
        <WaitingState label={t('answerSubmitted')} t={t} />
      ) : (
        <>
          <label htmlFor="text-answer">{t('yourAnswer')}</label>
          <textarea
            id="text-answer"
            value={text}
            maxLength={150}
            onChange={(event) => setText(event.target.value)}
          />
          <p className="character-count">{count}/150</p>
          <button
            type="button"
            onClick={() => void submit()}
            disabled={
              !text.trim() ||
              count > 150 ||
              submission === 'submitting' ||
              status !== 'connected'
            }
          >
            {submission === 'submitting' ? t('submitting') : t('submitAnswer')}
          </button>
          {submission === 'failed' && (
            <Retry
              onRetry={() => void submit()}
              t={t}
              message={t('submissionFailed')}
            />
          )}
        </>
      )}
    </GameShell>
  );
}

function TextVoting({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const authoritativeVoted =
    privateState?.questionInstanceId === questionId &&
    privateState?.hasSubmittedTextAnswerVote === true;
  const eligible =
    privateState?.questionInstanceId === questionId &&
    privateState?.isEligibleForTextAnswerVote === true;
  const [selectedId, setSelectedId] = useState<string>();
  const [submission, setSubmission] = useState<SubmissionState>(
    authoritativeVoted ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  const options = game.textResults?.votingOptions ?? [];
  async function submit(choiceId = selectedId): Promise<void> {
    if (!choiceId || !questionId || inFlight.current) return;
    inFlight.current = true;
    setSubmission('submitting');
    try {
      await gameHubConnection.submitTextAnswerVote(
        session,
        choiceId,
        questionId,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          questionId,
          'text-vote',
        ),
      );
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode));
      setSubmission('submitted');
    } catch {
      setSubmission('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(question?.text, locale)}</h2>
      {submission === 'submitted' || !eligible ? (
        <WaitingState
          label={
            submission === 'submitted'
              ? t('voteSubmitted')
              : t('notEligibleToVote')
          }
          t={t}
        />
      ) : (
        <fieldset>
          <legend>{t('vote')}</legend>
          <div className="option-list">
            {options.map((option) => (
              <label
                key={option.answerId}
                className={`text-option ${selectedId === option.answerId ? 'is-selected' : ''}`}
              >
                <input
                  type="radio"
                  name="text-vote"
                  value={option.answerId}
                  checked={selectedId === option.answerId}
                  onChange={() => {
                    setSelectedId(option.answerId);
                    void submit(option.answerId);
                  }}
                />
                {option.text}
              </label>
            ))}
          </div>
          {submission === 'failed' && (
            <Retry
              onRetry={() => void submit()}
              t={t}
              message={t('voteFailed')}
            />
          )}
        </fieldset>
      )}
    </GameShell>
  );
}

function PhotoAnswer({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const authoritativeSubmitted = privateState?.hasSubmittedPhotoAnswer === true;
  const [file, setFile] = useState<Blob>();
  const [preview, setPreview] = useState<string>();
  const [failure, setFailure] = useState<TranslationKey>();
  const [state, setState] = useState<
    'idle' | 'processing' | 'uploading' | 'submitted' | 'failed'
  >(authoritativeSubmitted ? 'submitted' : 'idle');
  const inFlight = useRef(false);
  useEffect(
    () => () => {
      if (preview) URL.revokeObjectURL(preview);
    },
    [preview],
  );
  async function select(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const selected = event.target.files?.[0];
    event.target.value = '';
    if (!selected) return;
    setFailure(undefined);
    setState('processing');
    try {
      const prepared = await preparePhotoAnswer(selected);
      if (preview) URL.revokeObjectURL(preview);
      setFile(prepared);
      setPreview(URL.createObjectURL(prepared));
      setState('idle');
    } catch (error) {
      setFailure(photoErrorMessage(error));
      setState('failed');
    }
  }
  async function submit(): Promise<void> {
    if (!file || !questionId || inFlight.current) return;
    inFlight.current = true;
    setFailure(undefined);
    setState('uploading');
    try {
      const result = await uploadPhotoAnswer(
        session,
        questionId,
        file,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          questionId,
          'photo-answer',
        ),
      );
      onSnapshot(result.roomSnapshot);
      setState('submitted');
    } catch {
      setFailure('photoUploadFailed');
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(question?.text, locale)}</h2>
      {state === 'submitted' || authoritativeSubmitted ? (
        <WaitingState
          label={t('photoSubmitted')}
          t={t}
          preview={preview}
          previewAlt={t('photoPreview')}
        />
      ) : (
        <>
          <label className="file-button" htmlFor="photo-answer">
            {t('takePhoto')}
          </label>
          <input
            id="photo-answer"
            className="visually-hidden"
            type="file"
            accept="image/*"
            capture="environment"
            onChange={select}
          />
          {state === 'processing' && (
            <p role="status">{t('processingPhoto')}</p>
          )}
          {preview && (
            <>
              <img
                className="media-preview"
                src={preview}
                alt={t('photoPreview')}
              />
              <button
                type="button"
                className="secondary-action"
                onClick={() => {
                  setFile(undefined);
                  URL.revokeObjectURL(preview);
                  setPreview(undefined);
                }}
              >
                {t('changePhoto')}
              </button>
              <button
                type="button"
                onClick={() => void submit()}
                disabled={state === 'uploading' || status !== 'connected'}
              >
                {state === 'uploading' ? t('uploadingPhoto') : t('submitPhoto')}
              </button>
            </>
          )}
          {failure && (
            <Retry onRetry={() => void submit()} t={t} message={t(failure)} />
          )}
        </>
      )}
    </GameShell>
  );
}

function DrawingAnswer({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const privateStateMatchesQuestion =
    privateState?.questionInstanceId === questionId;
  const authoritativeSubmitted =
    privateStateMatchesQuestion &&
    privateState?.hasSubmittedDrawingAnswer === true;
  const eligible =
    privateStateMatchesQuestion &&
    privateState?.isEligibleForDrawingAnswer !== false;
  const [canvas, setCanvas] = useState<HTMLCanvasElement>();
  const [hasInk, setHasInk] = useState(false);
  const [preview, setPreview] = useState<string>();
  const [state, setState] = useState<SubmissionState>(
    authoritativeSubmitted ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  useEffect(
    () => () => {
      if (preview) URL.revokeObjectURL(preview);
    },
    [preview],
  );
  async function submit(): Promise<void> {
    if (!canvas || !questionId || !hasInk || inFlight.current) return;
    inFlight.current = true;
    setState('submitting');
    try {
      const png = await drawingPng(canvas, hasInk);
      setPreview(URL.createObjectURL(png));
      const result = await uploadDrawingAnswer(
        session,
        questionId,
        png,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          questionId,
          'drawing-answer',
        ),
      );
      onSnapshot(result.roomSnapshot);
      setState('submitted');
    } catch {
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t} className="game-card--drawing">
      <h2>{localizedText(question?.text, locale)}</h2>
      {state === 'submitted' || authoritativeSubmitted || !eligible ? (
        <WaitingState
          label={
            state === 'submitted' || authoritativeSubmitted
              ? t('drawingSubmitted')
              : t('waitingForOthers')
          }
          t={t}
          preview={preview}
          previewAlt={t('drawingCanvas')}
        />
      ) : (
        <>
          <DrawingCanvas
            disabled={state === 'submitting'}
            onCanvas={setCanvas}
            onInkChange={setHasInk}
            labels={drawingLabels(t)}
          />
          <button
            type="button"
            onClick={() => void submit()}
            disabled={
              !hasInk || state === 'submitting' || status !== 'connected'
            }
          >
            {state === 'submitting' ? t('submitting') : t('submitDrawing')}
          </button>
          {state === 'failed' && (
            <Retry
              onRetry={() => void submit()}
              t={t}
              message={t('drawingSubmitFailed')}
            />
          )}
        </>
      )}
    </GameShell>
  );
}

function MediaVoting({
  kind,
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  kind: 'photo' | 'drawing';
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const question = gameQuestion(game);
  const questionId = question?.instanceId ?? question?.id;
  const submitted =
    kind === 'photo'
      ? privateState?.hasSubmittedPhotoAnswerVote === true
      : privateState?.hasSubmittedDrawingAnswerVote === true;
  const options =
    kind === 'photo'
      ? (game.photoAnswerResults?.anonymousOptions ?? []).map((option) => ({
          id: option.photoAnswerId,
          url: option.thumbnailPhotoUrl || option.displayPhotoUrl,
          label: `${t('vote')} ${option.displayOrder + 1}`,
        }))
      : (game.drawingAnswerResults?.anonymousOptions ?? []).map(
          (option, index) => ({
            id: option.drawingAnswerId,
            url: option.thumbnailDrawingUrl ?? option.displayDrawingUrl,
            label: `${t('vote')} ${(option.displayOrder ?? index) + 1}`,
          }),
        );
  const [selected, setSelected] = useState<string>();
  const [state, setState] = useState<SubmissionState>(
    submitted ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  async function submit(choiceId = selected): Promise<void> {
    if (!choiceId || !questionId || inFlight.current) return;
    inFlight.current = true;
    setState('submitting');
    try {
      if (kind === 'photo')
        await gameHubConnection.submitPhotoAnswerVote(
          session,
          choiceId,
          questionId,
          submissionIdentity(
            session.roomCode,
            session.playerId,
            questionId,
            'photo-vote',
          ),
        );
      else
        await gameHubConnection.submitDrawingAnswerVote(
          session,
          choiceId,
          questionId,
          submissionIdentity(
            session.roomCode,
            session.playerId,
            questionId,
            'drawing-vote',
          ),
        );
      onSnapshot(await gameHubConnection.getRoomSnapshot(session.roomCode));
      setState('submitted');
    } catch {
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(question?.text, locale)}</h2>
      {state === 'submitted' || submitted ? (
        <WaitingState label={t('voteSubmitted')} t={t} />
      ) : (
        <fieldset>
          <legend>{t('vote')}</legend>
          <div className="media-options">
            {options.map((option) => (
              <button
                type="button"
                className={`media-option ${selected === option.id ? 'is-selected' : ''}`}
                key={option.id}
                aria-pressed={selected === option.id}
                aria-label={option.label}
                onClick={() => {
                  setSelected(option.id);
                  void submit(option.id);
                }}
                disabled={state === 'submitting' || status !== 'connected'}
              >
                <RemoteMedia
                  src={option.url}
                  alt={option.label}
                  unavailable={t('mediaUnavailable')}
                  loading={t('mediaLoading')}
                />
              </button>
            ))}
          </div>
          {state === 'failed' && (
            <Retry
              onRetry={() => void submit()}
              t={t}
              message={t('voteFailed')}
            />
          )}
        </fieldset>
      )}
    </GameShell>
  );
}

function FinalSelfie({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
  onPrivateState,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
  onPrivateState?: (privateState: PlayerPrivateGameState) => void;
}) {
  const final = privateState?.finalRound;
  const [file, setFile] = useState<Blob>();
  const [preview, setPreview] = useState<string>();
  const [state, setState] = useState<SubmissionState>(
    final?.hasSubmittedSelfie ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  useEffect(
    () => () => {
      if (preview) URL.revokeObjectURL(preview);
    },
    [preview],
  );
  async function select(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const selected = event.target.files?.[0];
    event.target.value = '';
    if (!selected) return;
    try {
      setState('preparing');
      reportFinalSelfie('capture-start');
      const prepared = await preparePhotoAnswer(selected);
      setFile(prepared);
      setPreview(URL.createObjectURL(prepared));
      reportFinalSelfie('capture-ready', {
        bytes: prepared.size,
        type: prepared.type,
      });
      setState('idle');
    } catch {
      reportFinalSelfie('capture-failed');
      setState('failed');
    }
  }
  async function submit(): Promise<void> {
    if (!file || inFlight.current) return;
    inFlight.current = true;
    setState('submitting');
    const startedAt = performance.now();
    reportFinalSelfie('upload-start', { bytes: file.size, type: file.type });
    try {
      const result = await uploadFinalSelfie(
        session,
        file,
        submissionIdentity(
          session.roomCode,
          session.playerId,
          'final-selfie',
          'final-selfie',
        ),
      );
      if (result.playerPrivateGameState.finalRound?.hasSubmittedSelfie !== true)
        throw new Error('final-selfie-not-acknowledged');
      onPrivateState?.(result.playerPrivateGameState);
      onSnapshot(result.roomSnapshot);
      reportFinalSelfie('upload-acknowledged', {
        durationMs: Math.round(performance.now() - startedAt),
        mediaId: result.mediaId ?? 'absent',
      });
      setState('submitted');
    } catch {
      reportFinalSelfie('upload-failed', {
        durationMs: Math.round(performance.now() - startedAt),
      });
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }
  if (
    !final?.canSubmitSelfie ||
    final.hasSubmittedSelfie ||
    state === 'submitted'
  )
    return (
      <GameShell game={game} status={status} t={t}>
        <WaitingState
          label={
            final?.hasSubmittedSelfie || state === 'submitted'
              ? t('photoSubmitted')
              : t('waitingForOthers')
          }
          t={t}
        />
      </GameShell>
    );
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{localizedText(final.selfiePrompt, locale)}</h2>
      <p>{t('finalSelfieHint')}</p>
      <label className="file-button" htmlFor="final-selfie">
        {t('takePhoto')}
      </label>
      <input
        id="final-selfie"
        className="visually-hidden"
        type="file"
        accept="image/*"
        capture="user"
        onChange={select}
      />
      {preview && (
        <img className="media-preview" src={preview} alt={t('photoPreview')} />
      )}
      {state === 'preparing' && <p role="status">{t('processingPhoto')}</p>}
      {file && state !== 'preparing' && (
        <button
          type="button"
          onClick={() => void submit()}
          disabled={status !== 'connected' || state === 'submitting'}
        >
          {state === 'submitting' ? t('uploadingPhoto') : t('submitPhoto')}
        </button>
      )}
      {state === 'failed' && (
        <Retry
          onRetry={() => void submit()}
          t={t}
          message={t('photoUploadFailed')}
        />
      )}
    </GameShell>
  );
}

function reportFinalSelfie(
  event: string,
  details: Record<string, string | number> = {},
): void {
  // Keep diagnostics useful for mobile Safari investigations without exposing
  // reconnect credentials or image contents.
  console.info(
    '[PartyGame final-selfie]',
    JSON.stringify({ event, ...details }),
  );
}

function FinalEdit({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
  onRecover,
  recoveryFailed,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
  onRecover?: () => void;
  recoveryFailed: boolean;
}) {
  const final = privateState?.finalRound;
  const artifact = game.finalRound?.artifacts.find(
    (candidate) => candidate.artifactId === final?.assignedArtifactId,
  );
  const [canvas, setCanvas] = useState<HTMLCanvasElement>();
  const [hasInk, setHasInk] = useState(false);
  const [state, setState] = useState<SubmissionState>(
    final?.hasSubmittedEdit ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  const pass = game.finalRound?.currentPass;

  async function submit(): Promise<void> {
    if (!canvas || !hasInk || !final?.assignedArtifactId || inFlight.current)
      return;
    inFlight.current = true;
    setState('submitting');
    try {
      const identity = `${final.assignedArtifactId}:pass:${pass ?? 'unknown'}`;
      const result = await uploadFinalEdit(
        session,
        final.assignedArtifactId,
        await drawingPng(canvas, hasInk),
        submissionIdentity(
          session.roomCode,
          session.playerId,
          identity,
          'final-edit',
        ),
      );
      onSnapshot(result.roomSnapshot);
      setState('submitted');
    } catch {
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }

  if (
    !final?.assignedArtifactId ||
    final.hasSubmittedEdit ||
    state === 'submitted'
  )
    return (
      <GameShell game={game} status={status} t={t}>
        <WaitingState label={t('waitingForOthers')} t={t} />
      </GameShell>
    );
  // The private event and public snapshot can arrive in either order.  Do not
  // create a blank editor with an incomplete caption; the App recovery path
  // will reconcile the pair and a normal snapshot update will mount it.
  if (!final.sourceDisplayMediaUrl || !artifact)
    return (
      <GameShell game={game} status={status} t={t}>
        <p role={recoveryFailed ? 'alert' : 'status'}>
          {recoveryFailed ? t('finalAssignmentUnavailable') : t('gameLoading')}
        </p>
        {onRecover && (
          <button
            type="button"
            className="secondary-action"
            onClick={onRecover}
          >
            {t('retry')}
          </button>
        )}
      </GameShell>
    );
  const nickname = artifact.subjectNickname;
  const role = localizedText(artifact.targetRole, locale);
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>
        {t('finalEditCaption')
          .replace('{nickname}', nickname)
          .replace('{role}', role)}
      </h2>
      <DrawingCanvas
        backgroundImage={final.sourceDisplayMediaUrl}
        disabled={state === 'submitting'}
        onCanvas={setCanvas}
        onInkChange={setHasInk}
        labels={drawingLabels(t)}
      />
      <button
        type="button"
        onClick={() => void submit()}
        disabled={!hasInk || status !== 'connected' || state === 'submitting'}
      >
        {t('submitDrawing')}
      </button>
      {state === 'failed' && (
        <Retry
          onRetry={() => void submit()}
          t={t}
          message={t('drawingSubmitFailed')}
        />
      )}
    </GameShell>
  );
}

function drawingLabels(t: Translate) {
  return {
    canvas: t('drawingCanvas'),
    undo: t('undoDrawing'),
    redo: t('redoDrawing'),
    clear: t('clearDrawing'),
    clearConfirm: t('clearDrawingConfirm'),
    cancel: t('cancel'),
    eraser: t('eraserDrawing'),
    colorControl: t('drawingColorControl'),
    widthControl: t('drawingWidthControl'),
    toolControl: t('drawingToolControl'),
    moreControl: t('drawingMoreControl'),
    colors: {
      black: t('drawingColorBlack'),
      white: t('drawingColorWhite'),
      red: t('drawingColorRed'),
      blue: t('drawingColorBlue'),
      green: t('drawingColorGreen'),
      yellow: t('drawingColorYellow'),
      orange: t('drawingColorOrange'),
      purple: t('drawingColorPurple'),
      pink: t('drawingColorPink'),
      brown: t('drawingColorBrown'),
    },
    widths: {
      4: t('drawingWidthThin'),
      10: t('drawingWidthMedium'),
      22: t('drawingWidthThick'),
    },
  };
}

function finalEditAssignmentKey(
  game: GameSnapshot,
  privateState?: PlayerPrivateGameState,
): string {
  const final = privateState?.finalRound;
  // The source URL is versioned by immutable media id (/api/media/{id}/…), and
  // CurrentPass distinguishes an assignment even if the same artifact moves
  // again later in the round.
  return `${game.finalRound?.currentPass ?? 'unknown'}:${final?.assignedArtifactId ?? 'pending'}:${final?.sourceDisplayMediaUrl ?? 'pending'}`;
}

function FinalPresentation({
  game,
  locale,
  status,
  t,
}: {
  game: GameSnapshot;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
}) {
  return (
    <GameShell
      game={game}
      status={status}
      t={t}
      className="game-card--final-presentation"
    >
      <section className="results-stage final-presentation">
        <h1>{t('finalPresentation')}</h1>
        <div className="final-presentation-grid">
          {(game.finalRound?.artifacts ?? []).map((artifact, index) => (
            <article
              className="result-card final-artifact"
              style={{ '--motion-index': Math.min(index, 8) } as CSSProperties}
              key={artifact.artifactId}
            >
              {artifact.displayMediaUrl && (
                <img
                  className="result-media"
                  src={artifact.displayMediaUrl}
                  alt={artifact.subjectNickname}
                />
              )}
              <h2>
                {artifact.subjectNickname} {t('asRole')}{' '}
                {localizedText(artifact.targetRole, locale)}
              </h2>
            </article>
          ))}
        </div>
      </section>
    </GameShell>
  );
}

function FinalVoting({
  session,
  game,
  privateState,
  locale,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  privateState?: PlayerPrivateGameState;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const [selected, setSelected] = useState<string>();
  const [state, setState] = useState<SubmissionState>(
    privateState?.finalRound?.hasSubmittedVote ? 'submitted' : 'idle',
  );
  const inFlight = useRef(false);
  async function submit(choiceId = selected): Promise<void> {
    if (!choiceId || inFlight.current) return;
    inFlight.current = true;
    setState('submitting');
    try {
      onSnapshot(
        await submitFinalVote(
          session,
          choiceId,
          submissionIdentity(
            session.roomCode,
            session.playerId,
            choiceId,
            'final-vote',
          ),
        ),
      );
      setState('submitted');
    } catch {
      setState('failed');
    } finally {
      inFlight.current = false;
    }
  }
  if (state === 'submitted' || privateState?.finalRound?.hasSubmittedVote)
    return (
      <GameShell game={game} status={status} t={t}>
        <WaitingState label={t('voteSubmitted')} t={t} />
      </GameShell>
    );
  return (
    <GameShell game={game} status={status} t={t}>
      <h2>{t('vote')}</h2>
      <div className="media-options">
        {(game.finalRound?.artifacts ?? []).map((artifact) => (
          <button
            className={`media-option ${selected === artifact.artifactId ? 'is-selected' : ''}`}
            type="button"
            key={artifact.artifactId}
            onClick={() => {
              setSelected(artifact.artifactId);
              void submit(artifact.artifactId);
            }}
            disabled={state === 'submitting' || status !== 'connected'}
          >
            {artifact.thumbnailMediaUrl && (
              <img
                className="media-option-image"
                src={artifact.thumbnailMediaUrl}
                alt={`${artifact.subjectNickname} ${localizedText(artifact.targetRole, locale)}`}
              />
            )}
          </button>
        ))}
      </div>
      {state === 'failed' && (
        <Retry onRetry={() => void submit()} t={t} message={t('voteFailed')} />
      )}
    </GameShell>
  );
}

function FinalResults({
  game,
  locale,
  status,
  t,
}: {
  game: GameSnapshot;
  locale: Locale;
  status: ConnectionStatus;
  t: Translate;
}) {
  return (
    <GameShell game={game} status={status} t={t}>
      <section className="results-stage">
        <h1>{t('resultsTitle')}</h1>
        {(game.finalRound?.artifacts ?? []).map((artifact) => (
          <article className="result-card" key={artifact.artifactId}>
            <h2>
              {artifact.subjectNickname} {t('asRole')}{' '}
              {localizedText(artifact.targetRole, locale)}
            </h2>
            <p>
              {artifact.voteCount} {t('votes')}
            </p>
          </article>
        ))}
      </section>
    </GameShell>
  );
}

function Completed({
  session,
  game,
  room,
  status,
  t,
  onSnapshot,
}: {
  session: PlayerSession;
  game: GameSnapshot;
  room: RoomSnapshot;
  status: ConnectionStatus;
  t: Translate;
  onSnapshot: (snapshot: RoomSnapshot) => void;
}) {
  const host =
    room.players.find((player) => player.id === session.playerId)?.isHost ===
    true;
  const [working, setWorking] = useState(false);
  async function playAgain(): Promise<void> {
    setWorking(true);
    try {
      onSnapshot(await gameHubConnection.playAgain(session));
    } finally {
      setWorking(false);
    }
  }
  return (
    <GameShell game={game} status={status} t={t}>
      <CompletedStage game={game} room={room} t={t} />
      {host ? (
        <button
          type="button"
          onClick={() => void playAgain()}
          disabled={working || status !== 'connected'}
        >
          {t('playAgain')}
        </button>
      ) : (
        <p className="waiting-message" role="status">
          {t('waitingForHost')}
        </p>
      )}
    </GameShell>
  );
}

function RemoteMedia({
  src,
  alt,
  unavailable,
  loading,
}: {
  src?: string | null;
  alt: string;
  unavailable: string;
  loading: string;
}) {
  const [failedSource, setFailedSource] = useState<string>();
  const [loadedSource, setLoadedSource] = useState<string>();
  if (!src || failedSource === src)
    return (
      <span className="media-unavailable" role="img" aria-label={unavailable}>
        ⚠
      </span>
    );
  return (
    <span className="remote-media">
      {loadedSource !== src && (
        <span className="media-loading" role="status">
          {loading}
        </span>
      )}
      <img
        className="media-option-image"
        src={src}
        alt={alt}
        onLoad={() => setLoadedSource(src)}
        onError={() => setFailedSource(src)}
      />
    </span>
  );
}

function photoErrorMessage(error: unknown): TranslationKey {
  if (!(error instanceof GameMediaError)) return 'photoProcessing';
  if (error.kind === 'unsupported') return 'photoInvalid';
  if (error.kind === 'too-large') return 'photoTooLarge';
  return 'photoProcessing';
}

function PlayerOption({
  player,
  selected,
  stateVersion,
  motionIndex,
  onSelect,
}: {
  player: PublicPlayer;
  selected: boolean;
  stateVersion: number;
  motionIndex: number;
  onSelect: () => void;
}) {
  const image = player.profilePhotoUrl
    ? `${player.profilePhotoUrl}${player.profilePhotoUrl.includes('?') ? '&' : '?'}v=${stateVersion}`
    : undefined;
  return (
    <button
      type="button"
      className={`player-option ${selected ? 'is-selected' : ''}`}
      style={{ '--motion-index': Math.min(motionIndex, 8) } as CSSProperties}
      aria-pressed={selected}
      onClick={onSelect}
    >
      {image ? (
        <img className="avatar" src={image} alt="" />
      ) : (
        <span className="avatar avatar--placeholder" aria-hidden="true">
          👤
        </span>
      )}
      <span>{player.nickname}</span>
      <span className="selection-check" aria-hidden="true">
        ✓
      </span>
    </button>
  );
}

function WaitingState({
  label,
  t,
  preview,
  previewAlt = '',
}: {
  label: string;
  t: Translate;
  preview?: string;
  previewAlt?: string;
}) {
  return (
    <div className="game-waiting submission-success" role="status">
      {preview && (
        <img className="submission-preview" src={preview} alt={previewAlt} />
      )}
      <span className="success-check" aria-hidden="true">
        ✓
      </span>
      <strong>{label}</strong>
      <p>{t('waitingForOthers')}</p>
    </div>
  );
}
function Retry({
  onRetry,
  t,
  message,
}: {
  onRetry: () => void;
  t: Translate;
  message: string;
}) {
  return (
    <p className="form-error" role="alert">
      {message}{' '}
      <button type="button" className="inline-button" onClick={onRetry}>
        {t('retry')}
      </button>
    </p>
  );
}

function Countdown({ game, t }: { game: GameSnapshot; t: Translate }) {
  const deadline = game.stageEndsAtUtc
    ? Date.parse(game.stageEndsAtUtc)
    : Number.NaN;
  const [now, setNow] = useState(() => gameHubConnection.serverNow());
  useEffect(() => {
    const timer = window.setInterval(
      () => setNow(gameHubConnection.serverNow()),
      250,
    );
    return () => window.clearInterval(timer);
  }, []);
  if (Number.isNaN(deadline)) return null;
  const seconds = Math.max(0, (deadline - now) / 1000);
  const urgency =
    seconds <= 5
      ? 'countdown--urgent'
      : seconds <= 10
        ? 'countdown--warning'
        : '';
  return (
    <p className={`countdown ${urgency}`} aria-live="off">
      {t('timeRemaining')}: <span>{seconds.toFixed(1)}s</span>
      {seconds === 0 ? ` — ${t('timerWaiting')}` : ''}
    </p>
  );
}
