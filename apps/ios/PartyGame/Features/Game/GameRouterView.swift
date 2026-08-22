import SwiftUI

struct GameRouterView: View {
    let store: GameSessionStore
    var body: some View {
        VStack(spacing: 0) {
            if let game = store.snapshot?.game {
                Group {
                    switch game.stage {
                case .categoryIntro:
                    CategoryIntroView(category: game.categories?.first)
                case .questionIntro:
                    QuestionIntroView(question: game.currentQuestion)
                case .collectingPlayerSelections:
                    CollectingPlayerSelectionsView(store: store, question: game.currentQuestion, players: store.snapshot?.players ?? [])
                case .showingQuestionResults:
                    ShowingQuestionResultsView(results: game.playerSelectionResults, players: store.snapshot?.players ?? [], store: store)
                case .roundSummary:
                    RoundSummaryView(summary: game.roundSummary, players: store.snapshot?.players ?? [])
                case .gameSummary:
                    GameSummaryView()
                case .pausedForDisplay:
                    PausedForDisplayView()
                case .completed:
                    CompletedView(store: store, summary: game.roundSummary, ranking: game.ranking, players: store.snapshot?.players ?? [])
                case .collectingTextAnswers:
                    CollectingTextAnswersView(store: store, question: game.currentQuestion)
                case .revealingTextAnswers:
                    RevealingTextAnswersView()
                case .collectingTextAnswerVotes:
                    CollectingTextAnswerVotesView(store: store, question: game.currentQuestion, results: game.textAnswerResults)
                case .showingTextAnswerResults:
                    ShowingTextAnswerResultsView(results: game.textAnswerResults, store: store)
                case .collectingPhotoAnswers:
                    if store.privateGameState?.questionInstanceId != game.resolvedQuestionInstanceId {
                        PhotoAnswerPrivateStateLoader()
                    } else if store.privateGameState?.hasSubmittedPhotoAnswer == true {
                        PhotoAnswerWaitingView(store: store, game: game)
                    } else {
                        PhotoAnswerCaptureView(store: store, game: game)
                    }
                case .revealingPhotoAnswers:
                    PhotoAnswerRevealWaitingView(game: game)
                case .collectingPhotoAnswerVotes:
                    if store.privateGameState?.questionInstanceId != game.resolvedQuestionInstanceId {
                        PhotoAnswerPrivateStateLoader()
                    } else if store.privateGameState?.hasSubmittedPhotoAnswerVote == true {
                        PhotoAnswerVoteWaitingView(game: game)
                    } else {
                        PhotoAnswerVotingView(store: store, game: game)
                    }
                case .showingPhotoAnswerResults:
                    PhotoAnswerResultsView(store: store, game: game)
                case .collectingDrawingAnswers:
                    if store.privateGameState?.questionInstanceId != game.resolvedQuestionInstanceId {
                        DrawingPrivateStateLoader(store: store, questionId: game.resolvedQuestionInstanceId)
                    } else if store.privateGameState?.hasSubmittedDrawingAnswer == true ||
                                store.privateGameState?.isEligibleForDrawingAnswer == false {
                        DrawingWaitingView(game: game)
                    } else {
                        DrawingAnswerTaskView(store: store, game: game)
                    }
                case .revealingDrawingAnswers:
                    DrawingRevealWaitingView(game: game)
                case .collectingDrawingAnswerVotes:
                    if store.privateGameState?.questionInstanceId != game.resolvedQuestionInstanceId {
                        DrawingPrivateStateLoader(store: store, questionId: game.resolvedQuestionInstanceId)
                    } else if store.privateGameState?.hasSubmittedDrawingAnswerVote == true {
                        DrawingVoteWaitingView(game: game)
                    } else {
                        DrawingVotingView(store: store, game: game)
                    }
                case .showingDrawingAnswerResults:
                    DrawingResultsView(store: store, game: game)
                case .collectingFinalSelfies:
                    if store.privateGameState?.finalRound?.hasSubmittedSelfie == true { FinalRoundWaitingView(game: game, message: "photoAnswer.sent") }
                    else if let final = store.privateGameState?.finalRound,
                            final.canSubmitSelfie == true,
                            let prompt = final.selfiePrompt,
                            !prompt.local.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        FinalRoundSelfieView(store: store, game: game, privateState: final)
                    } else {
                        FinalRoundSelfiePrivateStateLoader(store: store, game: game)
                    }
                case .collectingFinalEdits:
                    FinalRoundEditView(store: store, game: game)
                case .showingFinalPresentation:
                    FinalRoundPresentationView(store: store, game: game)
                case .collectingFinalVotes:
                    if store.privateGameState?.finalRound?.hasSubmittedVote == true { FinalRoundWaitingView(game: game, message: "finalRound.voteSaved") }
                    else { FinalRoundVotingView(store: store, game: game) }
                case .showingFinalResults:
                    FinalRoundResultsView(store: store, game: game)
                case .unknown(let val):
                    ProgressView().accessibilityLabel(String(format: String(localized: "game.awaiting_stage"), val))
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                
                Spacer(minLength: 12)
                
                if game.stage != .completed {
                    CountdownTimerView(stageEndsAtUtc: game.stageEndsAtUtc, serverOffset: store.serverOffset)
                        .padding()
                }
            } else {
                Text("game.loading")
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .padding(20).navigationBarBackButtonHidden()
        .overlay(alignment: .topLeading) {
            ZStack {
                Color.clear.frame(width: 1, height: 1)
                    .accessibilityElement()
                    .accessibilityIdentifier("game.started")
                Color.clear.frame(width: 1, height: 1)
                    .accessibilityElement()
                    .accessibilityIdentifier(snapshotIdentifier)
                Color.clear.frame(width: 1, height: 1)
                    .accessibilityElement()
                    .accessibilityIdentifier("game.connection|state=\(store.realtimeDiagnosticState)")
            }
        }
        .task(id: store.snapshot?.game?.resolvedQuestionInstanceId) {
            await PhotoAnswerImageCache.shared.clear()
        }
    }

    private var snapshotIdentifier: String {
        guard let snapshot = store.snapshot, let game = snapshot.game else { return "game.snapshot.unavailable" }
        return SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: String(describing: game.stage), questionId: game.resolvedQuestionInstanceId)
    }
}

private struct GameSummaryView: View {
    var body: some View {
        VStack(spacing: 12) {
            ProgressView()
            Text("game.awaiting_stage")
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("game-summary-view")
    }
}
