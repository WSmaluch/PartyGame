import XCTest
@testable import PartyGame

final class PhotoAnswerModelsTests: XCTestCase {
    private let questionId = "30000000-0000-0000-0000-000000000001"

    func testDecodesPhotoAnswerQuestionTypeAndUnknownValue() throws {
        XCTAssertEqual(try JSONDecoder().decode(QuestionType.self, from: Data(#""PhotoAnswer""#.utf8)), .photoAnswer)
        XCTAssertEqual(try JSONDecoder().decode(QuestionType.self, from: Data(#""FutureType""#.utf8)), .unknown("FutureType"))
    }

    func testDecodesAllPhotoAnswerStages() throws {
        let values: [(String, GameStage)] = [
            ("CollectingPhotoAnswers", .collectingPhotoAnswers), ("RevealingPhotoAnswers", .revealingPhotoAnswers),
            ("CollectingPhotoAnswerVotes", .collectingPhotoAnswerVotes), ("ShowingPhotoAnswerResults", .showingPhotoAnswerResults),
        ]
        for (raw, expected) in values { XCTAssertEqual(try JSONDecoder().decode(GameStage.self, from: Data("\"\(raw)\"".utf8)), expected) }
    }

    func testUnknownStageIsSafe() throws {
        XCTAssertEqual(try JSONDecoder().decode(GameStage.self, from: Data(#""FuturePhotoStage""#.utf8)), .unknown("FuturePhotoStage"))
    }

    func testDrawingAnswerValuesDecodeForStage5B() throws {
        XCTAssertEqual(try JSONDecoder().decode(QuestionType.self, from: Data(#""DrawingAnswer""#.utf8)), .drawingAnswer)
        for raw in ["CollectingDrawingAnswers", "RevealingDrawingAnswers", "CollectingDrawingAnswerVotes", "ShowingDrawingAnswerResults"] {
            XCTAssertNotEqual(try JSONDecoder().decode(GameStage.self, from: Data("\"\(raw)\"".utf8)), .unknown(raw))
        }
    }

    func testDecodesActualBackendLocalizedQuestionAndAnonymousOptions() throws {
        let extra = """
        ,
        "category":{"id":"10000000-0000-0000-0000-000000000001","name":{"pl":"Zabawa","en":"Fun"},"description":{"pl":"Opis","en":"Description"}},
        "question":{"id":"30000000-0000-0000-0000-000000000001","text":{"pl":"Zrób zdjęcie","en":"Take a photo"}},
        "photoAnswerResults":{"questionInstanceId":"30000000-0000-0000-0000-000000000001","submittedPlayers":2,"requiredPlayers":3,
        "anonymousOptions":[{"photoAnswerId":"40000000-0000-0000-0000-000000000001","displayPhotoUrl":"/api/media/x/display","thumbnailPhotoUrl":"/api/media/x/thumbnail","displayOrder":0,"width":900,"height":1600}]}
        """
        let game = try decodeGame(stage: "RevealingPhotoAnswers", extra: extra)
        XCTAssertEqual(game.currentQuestion?.instanceId.uuidString.lowercased(), questionId)
        XCTAssertEqual(game.photoAnswerResults?.anonymousOptions?.first?.width, 900)
        XCTAssertNil(game.photoAnswerResults?.options)
    }

    func testAnonymousDTOIgnoresInjectedAuthorFields() throws {
        let data = Data(#"{"photoAnswerId":"40000000-0000-0000-0000-000000000001","displayPhotoUrl":"/d","thumbnailPhotoUrl":"/t","displayOrder":0,"width":800,"height":800,"authorNickname":"SECRET"}"#.utf8)
        let option = try JSONDecoder().decode(AnonymousPhotoAnswer.self, from: data)
        XCTAssertEqual(option.displayOrder, 0)
    }

    func testDecodesResultsAuthorsVotersPointsDimensionsAndOptionalURLs() throws {
        let json = #"{"photoAnswerId":"40000000-0000-0000-0000-000000000001","displayPhotoUrl":null,"thumbnailPhotoUrl":"/t","width":1600,"height":900,"authorPlayerId":"50000000-0000-0000-0000-000000000001","authorNickname":"Ola","authorPhotoUrl":null,"voteCount":2,"isTopResult":true,"voters":[{"playerId":"60000000-0000-0000-0000-000000000001","nickname":"Jan","profilePhotoUrl":null,"pointsAwarded":200}]}"#
        let result = try JSONDecoder().decode(PhotoAnswerResultOption.self, from: Data(json.utf8))
        XCTAssertNil(result.displayPhotoUrl)
        XCTAssertEqual(result.authorNickname, "Ola")
        XCTAssertEqual(result.voters.first?.pointsAwarded, 200)
        XCTAssertEqual(result.width, 1600)
    }

    func testOldPrivateStateWithoutPhotoFieldsStillDecodes() throws {
        let json = #"{"playerId":"20000000-0000-0000-0000-000000000001","questionInstanceId":null,"hasSubmittedTextAnswer":false,"ownTextAnswerId":null,"hasSubmittedTextAnswerVote":false}"#
        let state = try JSONDecoder().decode(PlayerPrivateGameState.self, from: Data(json.utf8))
        XCTAssertFalse(state.hasSubmittedPhotoAnswer)
        XCTAssertNil(state.ownPhotoAnswerId)
    }

    func testDecodesPhotoPrivateState() throws {
        let json = #"{"playerId":"20000000-0000-0000-0000-000000000001","questionInstanceId":"30000000-0000-0000-0000-000000000001","hasSubmittedPhotoAnswer":true,"ownPhotoAnswerId":"40000000-0000-0000-0000-000000000001","hasSubmittedPhotoAnswerVote":true}"#
        let state = try JSONDecoder().decode(PlayerPrivateGameState.self, from: Data(json.utf8))
        XCTAssertTrue(state.hasSubmittedPhotoAnswer)
        XCTAssertNotNil(state.ownPhotoAnswerId)
        XCTAssertTrue(state.hasSubmittedPhotoAnswerVote)
    }

    func testMissingOptionalPhotoSectionKeepsOldSnapshotCompatible() throws {
        XCTAssertNil(try decodeGame(stage: "CollectingTextAnswers", extra: "").photoAnswerResults)
    }

    func testSnapshotAccumulatorUsesTwentyThenRejectsEighteenThenAcceptsTwentyOne() {
        var accumulator = SnapshotAccumulator()
        XCTAssertTrue(accumulator.accept(room(version: 20)))
        XCTAssertFalse(accumulator.accept(room(version: 18)))
        XCTAssertTrue(accumulator.accept(room(version: 21)))
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 21)
    }

    private func decodeGame(stage: String, extra: String) throws -> GameSnapshot {
        let json = """
        {"stage":"\(stage)","currentRoundNumber":1,"totalRounds":1,"currentQuestionNumber":1,"questionsInCurrentRound":4,
        "stageEndsAtUtc":null,"pausedAtUtc":null,"pausedStage":null,"pausedRemainingMilliseconds":null,"scores":[]\(extra)}
        """
        return try JSONDecoder().decode(GameSnapshot.self, from: Data(json.utf8))
    }

    private func room(version: Int64) -> RoomSnapshot {
        RoomSnapshot(roomCode: "ABCD", phase: .started, stateVersion: version, displayConnected: true, minimumPlayers: 3,
                     maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [], createdAtUtc: "", startedAtUtc: "", game: nil)
    }
}
