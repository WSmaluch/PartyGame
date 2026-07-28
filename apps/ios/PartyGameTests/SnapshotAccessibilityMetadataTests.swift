import XCTest
@testable import PartyGame

final class SnapshotAccessibilityMetadataTests: XCTestCase {
    func testLobbyIdentifierContainsVersionLobbyAndEmptyQuestionId() {
        let snapshot = fixture(phase: .lobby, stateVersion: 12, game: nil)

        XCTAssertEqual(
            SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: snapshot.phase.rawValue, questionId: nil),
            "game.snapshot|stateVersion=12|phase=Lobby|questionId="
        )
    }

    func testActiveGameIdentifierContainsVersionActualStageAndQuestionId() {
        let questionId = UUID(uuidString: "00000000-0000-0000-0000-000000000042")!
        let game = game(stage: .collectingTextAnswers, questionId: questionId)
        let snapshot = fixture(phase: .started, stateVersion: 27, game: game)

        XCTAssertEqual(
            SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: String(describing: game.stage), questionId: game.resolvedQuestionInstanceId),
            "game.snapshot|stateVersion=27|phase=collectingTextAnswers|questionId=00000000-0000-0000-0000-000000000042"
        )
    }

    func testCompletedIdentifierHasNoActiveQuestionId() {
        let game = game(stage: .completed, questionId: nil)
        let snapshot = fixture(phase: .completed, stateVersion: 41, game: game)

        XCTAssertEqual(
            SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: String(describing: game.stage), questionId: game.resolvedQuestionInstanceId),
            "game.snapshot|stateVersion=41|phase=completed|questionId="
        )
    }

    func testSameSnapshotProducesIdenticalIdentifier() {
        let game = game(stage: .collectingPlayerSelections, questionId: UUID())
        let snapshot = fixture(phase: .started, stateVersion: 8, game: game)

        let first = SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: String(describing: game.stage), questionId: game.resolvedQuestionInstanceId)
        let second = SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: String(describing: game.stage), questionId: game.resolvedQuestionInstanceId)

        XCTAssertEqual(first, second)
    }

    private func fixture(phase: RoomPhase, stateVersion: Int64, game: GameSnapshot?) -> RoomSnapshot {
        RoomSnapshot(
            roomCode: "ABCD", phase: phase, stateVersion: stateVersion, displayConnected: true,
            minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [],
            createdAtUtc: "2026-07-28T00:00:00Z", startedAtUtc: "2026-07-28T00:00:01Z", game: game
        )
    }

    private func game(stage: GameStage, questionId: UUID?) -> GameSnapshot {
        let question = questionId.map {
            GameQuestionSnapshot(
                instanceId: $0, categoryId: UUID(), questionText: LocalizedText(defaultText: "Pytanie", translations: nil),
                requiredAnswerType: "TextAnswer"
            )
        }
        return GameSnapshot(
            stage: stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 1, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil,
            pausedRemainingMilliseconds: nil, scores: [], categories: nil, currentQuestion: question,
            playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil
        )
    }
}
