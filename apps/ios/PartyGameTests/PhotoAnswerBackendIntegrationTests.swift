import XCTest
import UIKit
@testable import PartyGame

@MainActor
final class PhotoAnswerBackendIntegrationTests: XCTestCase {
    private struct ExternalAccess: Decodable {
        let roomCode: String
        let playerId: UUID
        let reconnectToken: String
        let nickname: String
    }

    func testFourQuestionPhotoFlowAgainstRealBackend() async throws {
        let accessPath = "/private/tmp/partygame-ios-integration-access.json"
        let baseURL = URL(string: "http://127.0.0.1:5050")!
        guard FileManager.default.fileExists(atPath: accessPath) else {
            XCTAssertNotEqual(ProcessInfo.processInfo.environment["PARTYGAME_IOS_INTEGRATION_REQUIRED"], "1",
                              "The orchestrated integration run requires its access fixture")
            return
        }
        let accessData = try await waitForFile(URL(fileURLWithPath: accessPath), timeout: 20)
        let access = try JSONDecoder().decode(ExternalAccess.self, from: accessData)
        let localSession = LocalPlayerSession(roomCode: access.roomCode, playerId: access.playerId,
            nickname: access.nickname, isHost: true, serverBaseURL: baseURL.absoluteString)
        let api = RoomAPIClient()
        let realtime = SignalRGameRealtimeClient()
        defer { Task { await realtime.disconnect() } }

        let jpeg = try XCTUnwrap(makeJPEG())
        var seenQuestions = Set<UUID>()
        for _ in 0 ..< 4 {
            let collecting = try await waitForStage(.collectingPhotoAnswers, newQuestionOutside: seenQuestions,
                                                     api: api, baseURL: baseURL, roomCode: access.roomCode)
            let questionId = try XCTUnwrap(collecting.photoAnswerResults?.questionInstanceId)
            XCTAssertTrue(seenQuestions.insert(questionId).inserted)
            let submissionId = UUID()
            let first = try await api.uploadPhotoAnswer(baseURL: baseURL, session: localSession,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId,
                clientSubmissionId: submissionId, jpegData: jpeg, progress: { _ in })
            XCTAssertTrue(first.playerPrivateGameState.hasSubmittedPhotoAnswer)
            XCTAssertEqual(first.playerPrivateGameState.ownPhotoAnswerId, first.photoAnswerId)

            let retry = try await api.uploadPhotoAnswer(baseURL: baseURL, session: localSession,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId,
                clientSubmissionId: submissionId, jpegData: jpeg, progress: { _ in })
            XCTAssertEqual(retry.photoAnswerId, first.photoAnswerId, "Retry must be idempotent")

            let resumedAfterUpload = try await api.resume(baseURL: baseURL, session: localSession,
                                                           reconnectToken: access.reconnectToken)
            XCTAssertTrue(resumedAfterUpload.privateState.hasSubmittedPhotoAnswer)
            XCTAssertEqual(resumedAfterUpload.privateState.ownPhotoAnswerId, first.photoAnswerId)

            if realtime.status != .connected {
                try await realtime.connect(baseURL: baseURL)
                _ = try await realtime.attachPlayer(roomCode: access.roomCode, playerId: access.playerId,
                                                    reconnectToken: access.reconnectToken)
            }

            let voting = try await waitForStage(.collectingPhotoAnswerVotes, api: api, baseURL: baseURL,
                                                 roomCode: access.roomCode)
            XCTAssertTrue(voting.photoAnswerResults?.anonymousOptions?.contains { $0.photoAnswerId == first.photoAnswerId } == true)
            try await realtime.submitPhotoAnswerVote(roomCode: access.roomCode, playerId: access.playerId,
                reconnectToken: access.reconnectToken, questionInstanceId: questionId, photoAnswerId: first.photoAnswerId)
            let privateAfterVote = try await waitForPrivateVote(api: api, baseURL: baseURL,
                session: localSession, token: access.reconnectToken, questionId: questionId)
            XCTAssertTrue(privateAfterVote.hasSubmittedPhotoAnswerVote)
            let results = try await waitForStage(.showingPhotoAnswerResults, api: api, baseURL: baseURL,
                                                 roomCode: access.roomCode)
            let options = try XCTUnwrap(results.photoAnswerResults?.options)
            XCTAssertEqual(options.reduce(0) { $0 + $1.voters.count }, 3)
            XCTAssertTrue(options.allSatisfy { !$0.authorNickname.isEmpty })
            XCTAssertTrue(options.flatMap(\.voters).allSatisfy { $0.pointsAwarded > 0 })
        }

        _ = try await waitForStage(.completed, api: api, baseURL: baseURL, roomCode: access.roomCode)
        XCTAssertEqual(seenQuestions.count, 4)
    }

    private func waitForStage(_ stage: GameStage, newQuestionOutside seen: Set<UUID> = [], api: RoomAPIClient,
                              baseURL: URL, roomCode: String, timeout: TimeInterval = 90) async throws -> GameSnapshot {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if let game = try await api.getRoom(baseURL: baseURL, roomCode: roomCode).game,
               game.stage == stage,
               stage == .completed || (game.currentQuestion.map { !seen.contains($0.instanceId) } ?? false) { return game }
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTFail("Timed out waiting for stage \(stage)")
        throw URLError(.timedOut)
    }

    private func waitForPrivateVote(api: RoomAPIClient, baseURL: URL, session: LocalPlayerSession,
                                    token: String, questionId: UUID) async throws -> PlayerPrivateGameState {
        let deadline = Date().addingTimeInterval(10)
        while Date() < deadline {
            let state = try await api.resume(baseURL: baseURL, session: session, reconnectToken: token).privateState
            if state.questionInstanceId == questionId, state.hasSubmittedPhotoAnswerVote { return state }
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

    private func makeJPEG() -> Data? {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        return UIGraphicsImageRenderer(size: CGSize(width: 640, height: 480), format: format)
            .image { context in
                UIColor.systemIndigo.setFill()
                context.fill(CGRect(x: 0, y: 0, width: 640, height: 480))
            }.jpegData(compressionQuality: 0.85)
    }
}
