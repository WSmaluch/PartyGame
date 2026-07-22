import XCTest
@testable import PartyGame

final class DrawingAnswerModelsTests: XCTestCase {
    func testDecodesQuestionTypeAndAllDrawingStages() throws {
        XCTAssertEqual(try decode(QuestionType.self, #""DrawingAnswer""#), .drawingAnswer)
        let expected: [(String, GameStage)] = [
            ("CollectingDrawingAnswers", .collectingDrawingAnswers),
            ("RevealingDrawingAnswers", .revealingDrawingAnswers),
            ("CollectingDrawingAnswerVotes", .collectingDrawingAnswerVotes),
            ("ShowingDrawingAnswerResults", .showingDrawingAnswerResults),
        ]
        for (raw, stage) in expected { XCTAssertEqual(try decode(GameStage.self, "\"\(raw)\""), stage) }
    }

    func testDecodesActualPublicAnonymousAndResultsContracts() throws {
        let game = try decode(GameSnapshot.self, """
        {"stage":"RevealingDrawingAnswers","currentRoundNumber":1,"totalRounds":1,"currentQuestionNumber":1,"questionsInCurrentRound":4,"stageEndsAtUtc":null,"pausedAtUtc":null,"pausedStage":null,"pausedRemainingMilliseconds":null,"scores":[],"drawingAnswerResults":{"questionInstanceId":"30000000-0000-0000-0000-000000000001","submittedPlayers":2,"requiredPlayers":3,"anonymousOptions":[{"drawingAnswerId":"40000000-0000-0000-0000-000000000001","displayDrawingUrl":"/media/d","thumbnailDrawingUrl":"/media/t","revealOrder":1,"width":1024,"height":1024}]}}
        """)
        XCTAssertEqual(game.drawingAnswerResults?.anonymousOptions?.first?.revealOrder, 1)
        XCTAssertNil(game.drawingAnswerResults?.options)
    }

    func testDecodesActualBackendProgressFieldNames() throws {
        let result = try decode(DrawingAnswerResultsSnapshot.self,
            #"{"questionInstanceId":"30000000-0000-0000-0000-000000000001","submittedPlayers":2,"requiredPlayers":3}"#)
        XCTAssertEqual(result.submittedDrawingAnswers, 2)
        XCTAssertEqual(result.requiredDrawingAnswers, 3)
    }

    func testDecodesActualBackendRoomEnvelopeWithLocalizedQuestion() throws {
        let room = try decode(RoomSnapshot.self, """
        {"roomCode":"ABCD","phase":"Started","stateVersion":9,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":1,"questionsPerRound":4,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":false,"finalDrawingPasses":1},"players":[],"createdAtUtc":"2026-07-21T12:00:00Z","startedAtUtc":"2026-07-21T12:01:00Z","game":{"stage":"CollectingDrawingAnswers","currentRoundNumber":1,"totalRounds":1,"currentQuestionNumber":1,"questionsInCurrentRound":4,"stageEndsAtUtc":null,"pausedAtUtc":null,"pausedStage":null,"pausedRemainingMilliseconds":null,"scores":[],"category":{"id":"10000000-0000-0000-0000-000000000001","name":{"pl":"Impreza","en":"Party"}},"question":{"id":"30000000-0000-0000-0000-000000000001","text":{"pl":"Narysuj logo","en":"Draw a logo"}},"drawingAnswerResults":{"questionInstanceId":"30000000-0000-0000-0000-000000000001","submittedPlayers":1,"requiredPlayers":3}}}
        """)
        XCTAssertEqual(room.game?.currentQuestion?.questionText.translations?["pl"], "Narysuj logo")
        XCTAssertEqual(room.game?.drawingAnswerResults?.submittedDrawingAnswers, 1)
    }

    func testAnonymousContractIgnoresInjectedIdentityAndStorageFields() throws {
        let option = try decode(AnonymousDrawingOption.self, #"{"drawingAnswerId":"40000000-0000-0000-0000-000000000001","displayDrawingUrl":"/d","thumbnailDrawingUrl":"/t","revealOrder":0,"width":1024,"height":1024,"authorNickname":"secret","storageKey":"secret"}"#)
        XCTAssertEqual(option.revealOrder, 0)
    }

    func testOldPrivateStateWithoutDrawingFieldsRemainsCompatible() throws {
        let state = try decode(PlayerPrivateGameState.self, #"{"playerId":"20000000-0000-0000-0000-000000000001","questionInstanceId":null,"hasSubmittedTextAnswer":false,"ownTextAnswerId":null,"hasSubmittedTextAnswerVote":false}"#)
        XCTAssertFalse(state.hasSubmittedDrawingAnswer)
        XCTAssertNil(state.ownDrawingAnswerId)
        XCTAssertFalse(state.hasSubmittedDrawingAnswerVote)
    }

    func testDecodesDrawingPrivateState() throws {
        let state = try decode(PlayerPrivateGameState.self, #"{"playerId":"20000000-0000-0000-0000-000000000001","questionInstanceId":"30000000-0000-0000-0000-000000000001","hasSubmittedTextAnswer":false,"ownTextAnswerId":null,"hasSubmittedTextAnswerVote":false,"hasSubmittedDrawingAnswer":true,"ownDrawingAnswerId":"40000000-0000-0000-0000-000000000001","hasSubmittedDrawingAnswerVote":true}"#)
        XCTAssertTrue(state.hasSubmittedDrawingAnswer)
        XCTAssertNotNil(state.ownDrawingAnswerId)
        XCTAssertTrue(state.hasSubmittedDrawingAnswerVote)
    }

    private func decode<T: Decodable>(_ type: T.Type, _ json: String) throws -> T {
        try JSONDecoder().decode(type, from: Data(json.utf8))
    }
}
