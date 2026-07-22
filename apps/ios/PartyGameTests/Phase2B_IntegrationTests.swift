import XCTest
@testable import PartyGame

final class Phase2B_IntegrationTests: XCTestCase {
    func testCreateRequestEncodingFixture() async throws {
        // Contract fixture only. This deliberately is not named E2E because it
        // does not connect to a backend.
        let createReq = CreateRoomRequest(nickname: "Host", settings: RoomSettings(), selectedPackageKeys: ["starter"])
        let encoder = JSONEncoder()
        let data = try encoder.encode(createReq)
        print("--- CAPTURED REQUEST BODY ---")
        print(String(data: data, encoding: .utf8)!)
        
    }
}


final class Phase3B_Tests: XCTestCase {
    @MainActor
    func testSubmitTextAnswer_UpdatesStore() async throws {
        let qId = UUID()
        let gameSnapshot = GameSnapshot(
            stage: .collectingTextAnswers,
            currentRoundNumber: 1,
            totalRounds: 1,
            currentQuestionNumber: 1,
            questionsInCurrentRound: 1,
            stageEndsAtUtc: nil,
            pausedAtUtc: nil,
            pausedStage: nil,
            pausedRemainingMilliseconds: nil,
            scores: [],
            categories: nil,
            currentQuestion: GameQuestionSnapshot(instanceId: qId, categoryId: UUID(), questionText: LocalizedText(defaultText: "Test", translations: nil), requiredAnswerType: "Text"),
            playerSelectionResults: nil,
            roundSummary: nil,
            textAnswerResults: nil
        )
        
        let pgs = PlayerPrivateGameState(playerId: UUID(), questionInstanceId: qId, hasSubmittedTextAnswer: true, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false)
        XCTAssertTrue(pgs.hasSubmittedTextAnswer)
        XCTAssertEqual(gameSnapshot.stage, .collectingTextAnswers)
    }
}
