import XCTest
@testable import PartyGame

@MainActor
final class DrawingAnswerBackendIntegrationTests: XCTestCase {
    private struct ExternalAccess: Decodable {
        let roomCode: String
        let playerId: UUID
        let reconnectToken: String
        let nickname: String
    }

    func testFourQuestionDrawingFlowAgainstRealBackend() async throws {
        let accessURL = URL(fileURLWithPath: "/private/tmp/partygame-ios-drawing-integration-access.json")
        guard FileManager.default.fileExists(atPath: accessURL.path) else {
            XCTAssertNotEqual(ProcessInfo.processInfo.environment["PARTYGAME_IOS_INTEGRATION_REQUIRED"], "1",
                              "The orchestrated integration run requires its access fixture")
            return
        }
        let baseURL = URL(string: "http://127.0.0.1:5050")!
        let access = try JSONDecoder().decode(ExternalAccess.self, from: try await waitForFile(accessURL, timeout: 20))
        let session = LocalPlayerSession(roomCode: access.roomCode, playerId: access.playerId,
            nickname: access.nickname, isHost: true, serverBaseURL: baseURL.absoluteString)
        let api = RoomAPIClient()
        let realtime = SignalRGameRealtimeClient()
        defer { Task { await realtime.disconnect() } }
        var seen = Set<UUID>()

        for questionIndex in 0 ..< 4 {
            let collecting = try await waitForStage(.collectingDrawingAnswers, excluding: seen, api: api, baseURL: baseURL, roomCode: access.roomCode)
            let questionId = try XCTUnwrap(collecting.drawingAnswerResults?.questionInstanceId)
            XCTAssertTrue(seen.insert(questionId).inserted)
            var canvas = DrawingCanvasState()
            canvas.selectedColor = .blue
            canvas.selectedLineWidth = .medium
            canvas.complete([DrawingPoint(x: 0.1, y: 0.2), DrawingPoint(x: 0.5, y: 0.8), DrawingPoint(x: 0.9, y: 0.2)])
            let png = try await DrawingRenderer().render(canvas)
            XCTAssertEqual(Array(png.prefix(8)), [137, 80, 78, 71, 13, 10, 26, 10])
            let submissionId = UUID()
            let first = try await api.uploadDrawingAnswer(baseURL: baseURL, session: session,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId,
                clientSubmissionId: submissionId, pngData: png, progress: { _ in })
            XCTAssertTrue(first.playerPrivateGameState.hasSubmittedDrawingAnswer)
            XCTAssertEqual(first.playerPrivateGameState.ownDrawingAnswerId, first.drawingAnswerId)
            let retry = try await api.uploadDrawingAnswer(baseURL: baseURL, session: session,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId,
                clientSubmissionId: submissionId, pngData: png, progress: { _ in })
            XCTAssertEqual(retry.drawingAnswerId, first.drawingAnswerId)

            if questionIndex == 0 {
                try await realtime.connect(baseURL: baseURL)
                _ = try await realtime.attachPlayer(roomCode: access.roomCode, playerId: access.playerId,
                    reconnectToken: access.reconnectToken)
                await realtime.disconnect()
                try await realtime.connect(baseURL: baseURL)
                _ = try await realtime.attachPlayer(roomCode: access.roomCode, playerId: access.playerId,
                    reconnectToken: access.reconnectToken)
            }

            let voting = try await waitForStage(.collectingDrawingAnswerVotes, api: api, baseURL: baseURL, roomCode: access.roomCode)
            XCTAssertTrue(voting.drawingAnswerResults?.anonymousOptions?.contains { $0.drawingAnswerId == first.drawingAnswerId } == true)
            XCTAssertTrue(voting.drawingAnswerResults?.anonymousOptions?.allSatisfy { $0.displayDrawingUrl != nil } == true)
            try await realtime.submitDrawingAnswerVote(roomCode: access.roomCode, playerId: access.playerId,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId, drawingAnswerId: first.drawingAnswerId)
            let resumed = try await api.resume(baseURL: baseURL, session: session, reconnectToken: access.reconnectToken)
            XCTAssertTrue(resumed.privateState.hasSubmittedDrawingAnswerVote)
            let results = try await waitForStage(.showingDrawingAnswerResults, api: api, baseURL: baseURL, roomCode: access.roomCode)
            let options = try XCTUnwrap(results.drawingAnswerResults?.options)
            XCTAssertEqual(options.reduce(0) { $0 + $1.voteCount }, 3)
            XCTAssertTrue(options.allSatisfy { !$0.authorNickname.isEmpty })
            XCTAssertTrue(options.flatMap(\.voters).allSatisfy { $0.pointsAwarded > 0 })
        }
        _ = try await waitForStage(.completed, api: api, baseURL: baseURL, roomCode: access.roomCode)
        XCTAssertEqual(seen.count, 4)
    }

    private func waitForStage(_ stage: GameStage, excluding seen: Set<UUID> = [], api: RoomAPIClient,
                              baseURL: URL, roomCode: String, timeout: TimeInterval = 120) async throws -> GameSnapshot {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if let game = try await api.getRoom(baseURL: baseURL, roomCode: roomCode).game,
               game.stage == stage,
               stage == .completed || (game.resolvedQuestionInstanceId.map { !seen.contains($0) } ?? false) { return game }
            try await Task.sleep(for: .milliseconds(100))
        }
        throw URLError(.timedOut)
    }

    private func waitForFile(_ url: URL, timeout: TimeInterval) async throws -> Data {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if let data = try? Data(contentsOf: url), !data.isEmpty { return data }
            try await Task.sleep(for: .milliseconds(100))
        }
        throw URLError(.fileDoesNotExist)
    }
}
