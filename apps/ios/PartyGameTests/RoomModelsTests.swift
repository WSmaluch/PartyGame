import XCTest
@testable import PartyGame

final class RoomModelsTests: XCTestCase {
    func testDecodesPublicRoomSnapshotContract() throws {
        let json = #"{"roomCode":"ABCD","phase":"Lobby","stateVersion":12,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":4,"questionsPerRound":5,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":true,"finalDrawingPasses":3},"players":[{"id":"0dc81d35-c68d-47c6-aebb-5e86407a1bb0","nickname":"Ola","isHost":true,"isReady":false,"isConnected":true,"hasProfilePhoto":true,"profilePhotoUrl":"/uploads/ola.jpg","score":0}],"createdAtUtc":"2026-07-20T12:00:00Z","startedAtUtc":null}"#

        let snapshot = try JSONDecoder().decode(RoomSnapshot.self, from: Data(json.utf8))

        XCTAssertEqual(snapshot.roomCode, "ABCD")
        XCTAssertEqual(snapshot.phase, .lobby)
        XCTAssertEqual(snapshot.stateVersion, 12)
        XCTAssertEqual(snapshot.players.first?.nickname, "Ola")
    }

    func testSnapshotAccumulatorAcceptsOnlyNewerVersions() {
        var accumulator = SnapshotAccumulator()
        let snap1 = fixture(version: 5)
        let snap2 = fixture(version: 4)
        let snap3 = fixture(version: 6)
        
        accumulator.accept(snap1)
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 5)
        
        accumulator.accept(snap2)
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 5)
        
        accumulator.accept(snap3)
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 6)
    }

    func testStateVersionProgression50_48_51() {
        var accumulator = SnapshotAccumulator()
        
        accumulator.accept(fixture(version: 50))
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 50)
        
        accumulator.accept(fixture(version: 48))
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 50)
        
        accumulator.accept(fixture(version: 51))
        XCTAssertEqual(accumulator.snapshot?.stateVersion, 51)
    }

    func testRoomSettingsValidateContractRanges() {
        XCTAssertTrue(RoomSettings().isValid)
        var invalid = RoomSettings()
        invalid.questionsPerRound = 3
        XCTAssertFalse(invalid.isValid)
        invalid = RoomSettings()
        invalid.drawingSeconds = 301
        XCTAssertFalse(invalid.isValid)
    }

    func testDecodesSignalRLocalizedQuestionAfterJSONSerializationRoundTrip() throws {
        let json = #"{"roomCode":"ABCD","phase":"Started","stateVersion":12,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":1,"questionsPerRound":4,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":false,"finalDrawingPasses":3},"players":[],"createdAtUtc":"2026-07-20T12:00:00Z","game":{"stage":"CollectingDrawingAnswers","question":{"id":"00000000-0000-0000-0000-000000000001","text":{"pl":"Narysuj kota","en":"Draw a cat"}},"drawingAnswerResults":{"questionInstanceId":"00000000-0000-0000-0000-000000000002","submittedPlayers":1,"requiredPlayers":3}}}"#
        let object = try JSONSerialization.jsonObject(with: Data(json.utf8))
        let signalRData = try JSONSerialization.data(withJSONObject: object)

        let snapshot = try JSONDecoder().decode(RoomSnapshot.self, from: signalRData)

        XCTAssertEqual(snapshot.game?.currentQuestion?.questionText.local.isEmpty, false)
        XCTAssertEqual(snapshot.game?.drawingAnswerResults?.submittedDrawingAnswers, 1)
    }

    func testDecodesCompletedGameRankingWithoutRoundSummary() throws {
        let json = #"{"roomCode":"ABCD","phase":"Completed","stateVersion":52,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":1,"questionsPerRound":4,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":false,"finalDrawingPasses":3},"players":[],"createdAtUtc":"2026-07-20T12:00:00Z","game":{"stage":"Completed","scores":[],"ranking":[{"playerId":"00000000-0000-0000-0000-000000000001","nickname":"Ola","profilePhotoUrl":null,"score":12,"rank":1}]}}"#

        let snapshot = try JSONDecoder().decode(RoomSnapshot.self, from: Data(json.utf8))

        XCTAssertNil(snapshot.game?.roundSummary)
        XCTAssertEqual(snapshot.game?.ranking?.count, 1)
        XCTAssertEqual(snapshot.game?.ranking?.first?.nickname, "Ola")
    }

    func testDecodesGameSummaryInsteadOfTreatingItAsAnUnknownStage() throws {
        let json = #"{"roomCode":"ABCD","phase":"Started","stateVersion":53,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":1,"questionsPerRound":4,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":false,"finalDrawingPasses":3},"players":[],"createdAtUtc":"2026-07-20T12:00:00Z","game":{"stage":"GameSummary","scores":[]}}"#

        let snapshot = try JSONDecoder().decode(RoomSnapshot.self, from: Data(json.utf8))

        XCTAssertEqual(snapshot.game?.stage, .gameSummary)
    }

    func testDecodesFinalRoundAssignmentAndPrivateReconnectState() throws {
        let json = #"{"roomCode":"ABCD","phase":"Started","stateVersion":54,"displayConnected":true,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":1,"questionsPerRound":4,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":true,"finalDrawingPasses":2},"players":[],"createdAtUtc":"2026-07-20T12:00:00Z","game":{"stage":"CollectingFinalEdits","scores":[],"finalRound":{"currentPass":1,"totalPasses":2,"submittedSelfies":3,"requiredSelfies":3,"submittedEdits":0,"requiredEdits":3,"submittedVotes":0,"requiredVotes":0,"artifacts":[],"editAssignments":[{"artifactId":"00000000-0000-0000-0000-000000000011","editorPlayerId":"00000000-0000-0000-0000-000000000012","sourceDisplayMediaUrl":"/api/media/a/display","sourceThumbnailMediaUrl":"/api/media/a/thumbnail"}]}}}"#
        let privateJSON = #"{"playerId":"00000000-0000-0000-0000-000000000012","questionInstanceId":"00000000-0000-0000-0000-000000000099","hasSubmittedTextAnswer":false,"hasSubmittedTextAnswerVote":false,"hasSubmittedPhotoAnswer":false,"hasSubmittedPhotoAnswerVote":false,"hasSubmittedDrawingAnswer":false,"hasSubmittedDrawingAnswerVote":false,"isEligibleForDrawingAnswer":false,"finalRound":{"hasSubmittedSelfie":true,"assignedArtifactId":"00000000-0000-0000-0000-000000000011","sourceDisplayMediaUrl":"/api/media/a/display","sourceThumbnailMediaUrl":"/api/media/a/thumbnail","hasSubmittedEdit":false,"hasSubmittedVote":false}}"#

        let snapshot = try JSONDecoder().decode(RoomSnapshot.self, from: Data(json.utf8))
        let privateState = try JSONDecoder().decode(PlayerPrivateGameState.self, from: Data(privateJSON.utf8))

        XCTAssertEqual(snapshot.game?.stage, .collectingFinalEdits)
        XCTAssertEqual(snapshot.game?.finalRound?.editAssignments?.first?.artifactId.uuidString, "00000000-0000-0000-0000-000000000011")
        XCTAssertEqual(privateState.finalRound?.assignedArtifactId?.uuidString, "00000000-0000-0000-0000-000000000011")
    }

    @MainActor
    func testRoomCodeNormalizationRemovesAmbiguousCharacters() {
        XCTAssertEqual(GameSessionStore.normalizedRoomCode("a1bi-cd"), "ABCD")
        XCTAssertEqual(GameSessionStore.normalizedRoomCode("xy9z77"), "XY9Z")
    }

    private func fixture(version: Int64) -> RoomSnapshot {
        RoomSnapshot(roomCode: "ABCD", phase: .lobby, stateVersion: version, displayConnected: false,
                     minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [],
                     createdAtUtc: "2026-07-20T12:00:00Z", startedAtUtc: nil, game: nil)
    }
}
