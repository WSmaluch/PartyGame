import XCTest
@testable import PartyGame

@MainActor
final class GameSessionStoreTests: XCTestCase {
    private var configuration: ServerConfiguration!
    private var defaults: UserDefaults!
    private var defaultsSuiteName = ""
    private var api: MockRoomAPIClient!
    private var realtime: MockGameRealtimeClient!
    private var storage: MockPlayerSessionStorage!
    private var store: GameSessionStore!

    override func setUp() async throws {
        try await super.setUp()
        defaultsSuiteName = "PartyGameTests.\(UUID().uuidString)"
        defaults = UserDefaults(suiteName: defaultsSuiteName)!
        configuration = ServerConfiguration(defaults: defaults)
        api = MockRoomAPIClient()
        realtime = MockGameRealtimeClient()
        storage = MockPlayerSessionStorage()
        store = GameSessionStore(configuration: configuration, api: api, realtime: realtime, storage: storage)
    }

    override func tearDown() async throws {
        await store?.shutdown()
        store = nil
        storage = nil
        realtime = nil
        api = nil
        configuration = nil
        if !defaultsSuiteName.isEmpty { defaults?.removePersistentDomain(forName: defaultsSuiteName) }
        defaults = nil
        try await super.tearDown()
    }

    func testCreateRoomSuccess() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: false, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot

        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)

        XCTAssertEqual(store.session?.roomCode, "TEST")
        XCTAssertEqual(store.screen, .profilePhoto)
        XCTAssertTrue(api.createRoomCalled)
        XCTAssertTrue(realtime.connectCalled)
        XCTAssertTrue(realtime.attachPlayerCalled)
        XCTAssertNotNil(storage.savedSession)
    }

    func testJoinRoomSuccess() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Jan", isHost: false, isReady: false, isConnected: true, hasProfilePhoto: false, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        api.joinRoomResult = JoinRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot

        await store.joinRoom(roomCode: "test", nickname: "Jan")

        XCTAssertEqual(store.session?.roomCode, "TEST")
        XCTAssertEqual(store.screen, .profilePhoto)
        XCTAssertTrue(api.joinRoomCalled)
        XCTAssertEqual(api.joinRoomCode, "TEST")
        XCTAssertTrue(realtime.connectCalled)
        XCTAssertTrue(realtime.attachPlayerCalled)
    }

    func testUploadPhotoTransitionsToLobby() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)

        api.uploadProfilePhotoResult = snapshot
        await store.uploadProfilePhoto(Data())

        XCTAssertEqual(store.screen, .lobby)
        XCTAssertTrue(api.uploadProfilePhotoCalled)
    }

    func testSetReadyUpdatesSnapshot() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        api.uploadProfilePhotoResult = snapshot
        await store.uploadProfilePhoto(Data())

        realtime.setReadyResult = snapshot
        await store.setReady(true)

        XCTAssertTrue(realtime.setReadyCalled)
        XCTAssertEqual(store.ownPlayer?.isReady, true)
    }

    func testRoomStartedTransitionsToStarted() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .started, stateVersion: 2, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: true, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: "2026", game: nil)
        
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        
        realtime.onRoomStarted?(snapshot)
        
        XCTAssertEqual(store.screen, .started)
    }

    func testRestoreSession() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        let localSession = LocalPlayerSession(roomCode: "TEST", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: "http://test")
        storage.savedSession = (localSession, "token")
        api.resumeResult = ResumePlayerResponse(player: snapshot.players[0], snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot

        await store.restoreSession()

        XCTAssertEqual(store.session?.roomCode, "TEST")
        XCTAssertEqual(store.screen, .lobby)
        XCTAssertTrue(realtime.attachPlayerCalled)
    }

    func testSnapshotAccumulatorIgnoresOldVersion() async {
        let playerId = UUID()
        let snapshot1 = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 2, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: false, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        let snapshot2 = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true, hasProfilePhoto: false, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot1, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot1
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        
        XCTAssertEqual(store.snapshot?.stateVersion, 2)
        XCTAssertEqual(store.snapshot?.players[0].isReady, false)
        
        // Obsolete snapshot should be ignored
        store.apply(snapshot2)
        XCTAssertEqual(store.snapshot?.stateVersion, 2)
        XCTAssertEqual(store.snapshot?.players[0].isReady, false)
    }

    func testAppliesPhotoResultsThenNewerDrawingSnapshot() {
        let playerId = UUID()
        let photo = roomSnapshot(playerId: playerId, version: 40, game: game(stage: .showingPhotoAnswerResults, questionId: UUID()))
        let drawing = roomSnapshot(playerId: playerId, version: 41, game: game(stage: .collectingDrawingAnswers, questionId: UUID()))

        store.apply(photo)
        store.apply(drawing)

        XCTAssertEqual(store.snapshot?.stateVersion, 41)
        XCTAssertEqual(store.snapshot?.game?.stage, .collectingDrawingAnswers)
    }

    func testDelayedPhotoResultsSnapshotCannotOverwriteNewerDrawingSnapshot() {
        let playerId = UUID()
        let drawing = roomSnapshot(playerId: playerId, version: 41, game: game(stage: .collectingDrawingAnswers, questionId: UUID()))
        let delayedPhoto = roomSnapshot(playerId: playerId, version: 40, game: game(stage: .showingPhotoAnswerResults, questionId: UUID()))

        store.apply(drawing)
        store.apply(delayedPhoto)

        XCTAssertEqual(store.snapshot?.stateVersion, 41)
        XCTAssertEqual(store.snapshot?.game?.stage, .collectingDrawingAnswers)
    }

    func testReconnectRecoveryAppliesHigherSnapshotWithoutIntermediatePhase() async {
        let playerId = UUID()
        let photo = roomSnapshot(playerId: playerId, version: 40, game: game(stage: .showingPhotoAnswerResults, questionId: UUID()))
        let completed = completedSnapshot(playerId: playerId, version: 42)
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: photo,
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = photo
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)

        realtime.attachPlayerResult = completed
        api.resumeResult = ResumePlayerResponse(player: completed.players[0], snapshot: completed,
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        await store.retryConnection()

        XCTAssertEqual(store.snapshot?.stateVersion, 42)
        XCTAssertEqual(store.snapshot?.game?.stage, .completed)
    }

    func testRealtimeCallbackAppliesHigherSnapshotOnMainActor() async {
        let playerId = UUID()
        let completed = completedSnapshot(playerId: playerId, version: 42)
        await Task { @MainActor in realtime.onSnapshot?(completed) }.value

        XCTAssertTrue(Thread.isMainThread)
        XCTAssertEqual(store.snapshot?.stateVersion, 42)
        XCTAssertEqual(store.snapshot?.game?.stage, .completed)
    }

    func testApplicationBecameActiveRetriesConnection() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        let localSession = LocalPlayerSession(roomCode: "TEST", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: "http://test")
        storage.savedSession = (localSession, "token")
        api.resumeResult = ResumePlayerResponse(player: snapshot.players[0], snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot

        await store.restoreSession()
        realtime.status = .disconnected // Simulate disconnect
        realtime.onStatusChanged?(.disconnected)
        
        realtime.connectCalled = false
        realtime.getRoomSnapshotResult = snapshot
        await store.applicationBecameActive()
        
        XCTAssertTrue(realtime.connectCalled)
    }

    func testServerAddressChangedDisconnects() async {
        let playerId = UUID()
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .lobby, stateVersion: 1, displayConnected: false, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(), players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: nil, game: nil)
        
        let localSession = LocalPlayerSession(roomCode: "TEST", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: "http://old-server")
        storage.savedSession = (localSession, "token")
        api.resumeResult = ResumePlayerResponse(player: snapshot.players[0], snapshot: snapshot, privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = snapshot

        await store.restoreSession()
        
        configuration.baseURL = "http://new-server"
        await store.serverAddressChanged()
        
        XCTAssertTrue(realtime.disconnectCalled)
        XCTAssertEqual(store.realtimeStatus, .disconnected)
    }

    func testPrivatePhotoStateRejectsOtherPlayerOldQuestionAndFalseRegression() async {
        let playerId = UUID(), questionId = UUID(), photoId = UUID()
        let game = GameSnapshot(stage: .collectingPhotoAnswerVotes, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
                                questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil,
                                pausedRemainingMilliseconds: nil, scores: [], categories: nil,
                                currentQuestion: GameQuestionSnapshot(instanceId: questionId, categoryId: UUID(),
                                    questionText: LocalizedText(defaultText: "Photo", translations: nil), requiredAnswerType: "PhotoAnswer"),
                                playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil,
                                photoAnswerResults: PhotoAnswerResults(questionInstanceId: questionId, submittedPlayers: 3, requiredPlayers: 3,
                                    votedPlayers: 1, requiredVoters: 3, missingSubmissionPlayers: 0, missingVotePlayers: 2,
                                    highestVoteCount: nil, options: nil, anonymousOptions: []))
        let snapshot = RoomSnapshot(roomCode: "TEST", phase: .started, stateVersion: 1, displayConnected: true, minimumPlayers: 3,
            maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true,
                                 hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)],
            createdAtUtc: "", startedAtUtc: "", game: game)
        let accepted = PlayerPrivateGameState(playerId: playerId, questionInstanceId: questionId, hasSubmittedTextAnswer: false,
            ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false, hasSubmittedPhotoAnswer: true,
            ownPhotoAnswerId: photoId, hasSubmittedPhotoAnswerVote: true)
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot, privateState: accepted)
        realtime.attachPlayerResult = snapshot
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)

        realtime.onPlayerPrivateGameStateUpdated?(PlayerPrivateGameState(playerId: playerId, questionInstanceId: questionId,
            hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.onPlayerPrivateGameStateUpdated?(PlayerPrivateGameState(playerId: UUID(), questionInstanceId: questionId,
            hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))

        XCTAssertTrue(store.privateGameState?.hasSubmittedPhotoAnswer == true)
        XCTAssertTrue(store.privateGameState?.hasSubmittedPhotoAnswerVote == true)
        XCTAssertEqual(store.privateGameState?.ownPhotoAnswerId, photoId)
    }

    func testChangingQuestionClearsPhotoPrivateStateAndRejectsOldEvent() async {
        let playerId = UUID(), firstQuestion = UUID(), secondQuestion = UUID()
        func snapshot(version: Int64, question: UUID) -> RoomSnapshot {
            let game = GameSnapshot(stage: .collectingPhotoAnswers, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: Int(version),
                questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil, pausedRemainingMilliseconds: nil,
                scores: [], categories: nil, currentQuestion: GameQuestionSnapshot(instanceId: question, categoryId: UUID(),
                    questionText: LocalizedText(defaultText: "Photo", translations: nil), requiredAnswerType: "PhotoAnswer"),
                playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil)
            return RoomSnapshot(roomCode: "TEST", phase: .started, stateVersion: version, displayConnected: true, minimumPlayers: 3,
                maximumPlayers: 8, canStart: false, settings: RoomSettings(),
                players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true,
                    hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: "", game: game)
        }
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: snapshot(version: 1, question: firstQuestion),
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: firstQuestion, hasSubmittedTextAnswer: false,
                ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false, hasSubmittedPhotoAnswer: true))
        realtime.attachPlayerResult = snapshot(version: 1, question: firstQuestion)
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        store.apply(snapshot(version: 2, question: secondQuestion))
        realtime.onPlayerPrivateGameStateUpdated?(PlayerPrivateGameState(playerId: playerId, questionInstanceId: firstQuestion,
            hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false, hasSubmittedPhotoAnswer: true))
        XCTAssertNil(store.privateGameState)
    }

    func testPrivateStateRefreshRetriesAfterTransientResumeFailure() async {
        let playerId = UUID(), questionId = UUID()
        let lobby = roomSnapshot(playerId: playerId, version: 1, game: nil)
        let active = roomSnapshot(playerId: playerId, version: 2, game: drawingGame(questionId: questionId))
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: lobby,
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = lobby
        api.resumeResults = [
            .failure(MockError.missingStub("transient")),
            .success(ResumePlayerResponse(player: active.players[0], snapshot: active,
                privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: questionId, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false)))
        ]
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        store.apply(active)
        try? await Task.sleep(for: .seconds(2))

        XCTAssertGreaterThanOrEqual(api.resumeCallCount, 2)
        XCTAssertEqual(store.privateGameState?.questionInstanceId, questionId)
        XCTAssertNil(store.privateStateRefreshFailedQuestionId)
    }

    func testPrivateStateRefreshStopsAfterBoundedFailures() async {
        let playerId = UUID(), questionId = UUID()
        let lobby = roomSnapshot(playerId: playerId, version: 1, game: nil)
        let active = roomSnapshot(playerId: playerId, version: 2, game: drawingGame(questionId: questionId))
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: lobby,
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: nil, hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = lobby
        api.resumeResults = Array(repeating: .failure(MockError.missingStub("unavailable")), count: 8)
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)
        store.apply(active)
        try? await Task.sleep(for: .seconds(3))

        XCTAssertEqual(store.privateStateRefreshFailedQuestionId, questionId)
        XCTAssertNil(store.privateGameState)
        XCTAssertGreaterThanOrEqual(api.resumeCallCount, 5)
    }

    func testTextAnswerRetryReusesSubmissionIdAndNewQuestionGetsNewId() async {
        let playerId = UUID(), firstQuestion = UUID(), secondQuestion = UUID()
        let first = roomSnapshot(playerId: playerId, version: 1, game: textGame(questionId: firstQuestion))
        api.createRoomResult = CreateRoomResponse(roomCode: "TEST", playerId: playerId, reconnectToken: "token", snapshot: first,
            privateState: PlayerPrivateGameState(playerId: playerId, questionInstanceId: firstQuestion, hasSubmittedTextAnswer: false,
                ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false))
        realtime.attachPlayerResult = first
        realtime.submitTextAnswerResult = first
        await store.createRoom(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil)

        await store.submitTextAnswer(text: "retry")
        await store.submitTextAnswer(text: "retry")
        XCTAssertEqual(realtime.textSubmissionIds.count, 2)
        XCTAssertEqual(realtime.textSubmissionIds[0], realtime.textSubmissionIds[1])

        let second = roomSnapshot(playerId: playerId, version: 2, game: textGame(questionId: secondQuestion))
        realtime.submitTextAnswerResult = second
        store.apply(second)
        await store.submitTextAnswer(text: "new question")
        XCTAssertEqual(realtime.textSubmissionIds.count, 3)
        XCTAssertNotEqual(realtime.textSubmissionIds[1], realtime.textSubmissionIds[2])
    }

    private func roomSnapshot(playerId: UUID, version: Int64, game: GameSnapshot?) -> RoomSnapshot {
        RoomSnapshot(roomCode: "TEST", phase: game == nil ? .lobby : .started, stateVersion: version, displayConnected: true,
            minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true,
                                 hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)],
            createdAtUtc: "", startedAtUtc: game == nil ? nil : "", game: game)
    }

    private func drawingGame(questionId: UUID) -> GameSnapshot {
        GameSnapshot(stage: .collectingDrawingAnswers, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil, pausedRemainingMilliseconds: nil,
            scores: [], categories: nil, currentQuestion: nil, playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil,
            drawingAnswerResults: DrawingAnswerResultsSnapshot(questionInstanceId: questionId, submittedDrawingAnswers: 0,
                requiredDrawingAnswers: 3, submittedDrawingAnswerPlayerIds: nil, votedPlayers: nil, requiredVoters: nil,
                highestVoteCount: nil, options: nil, anonymousOptions: nil))
    }

    private func textGame(questionId: UUID) -> GameSnapshot {
        GameSnapshot(stage: .collectingTextAnswers, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil, pausedRemainingMilliseconds: nil,
            scores: [], categories: nil, currentQuestion: GameQuestionSnapshot(instanceId: questionId, categoryId: UUID(),
                questionText: LocalizedText(defaultText: "Text", translations: nil), requiredAnswerType: "TextAnswer"),
            playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil)
    }

    private func game(stage: GameStage, questionId: UUID?) -> GameSnapshot {
        GameSnapshot(stage: stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil,
            pausedRemainingMilliseconds: nil, scores: [], categories: nil,
            currentQuestion: questionId.map { GameQuestionSnapshot(instanceId: $0, categoryId: UUID(), questionText: LocalizedText(defaultText: "Question", translations: nil), requiredAnswerType: "PhotoAnswer") },
            playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil)
    }

    private func completedSnapshot(playerId: UUID, version: Int64) -> RoomSnapshot {
        RoomSnapshot(roomCode: "TEST", phase: .completed, stateVersion: version, displayConnected: true,
            minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: [RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true,
                hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)], createdAtUtc: "", startedAtUtc: "",
            game: game(stage: .completed, questionId: nil))
    }
}

private final class MockRoomAPIClient: RoomAPIClientProtocol, @unchecked Sendable {
    var createRoomCalled = false
    var createRoomResult: CreateRoomResponse?
    func createRoom(baseURL: URL, request: CreateRoomRequest) async throws -> CreateRoomResponse {
        createRoomCalled = true
        return createRoomResult!
    }

    var joinRoomCalled = false
    var joinRoomCode: String?
    var joinRoomResult: JoinRoomResponse?
    func joinRoom(baseURL: URL, roomCode: String, request: JoinRoomRequest) async throws -> JoinRoomResponse {
        joinRoomCalled = true
        joinRoomCode = roomCode
        return joinRoomResult!
    }

    func getRoom(baseURL: URL, roomCode: String) async throws -> RoomSnapshot {
        fatalError()
    }

    var resumeResult: ResumePlayerResponse?
    var resumeResults: [Result<ResumePlayerResponse, Error>] = []
    private(set) var resumeCallCount = 0
    func resume(baseURL: URL, session: LocalPlayerSession, reconnectToken: String) async throws -> ResumePlayerResponse {
        resumeCallCount += 1
        if !resumeResults.isEmpty { return try resumeResults.removeFirst().get() }
        guard let resumeResult else { throw MockError.missingStub("resume") }
        return resumeResult
    }

    var uploadProfilePhotoCalled = false
    var uploadProfilePhotoResult: RoomSnapshot?
    func uploadProfilePhoto(baseURL: URL, session: LocalPlayerSession, reconnectToken: String, jpegData: Data) async throws -> RoomSnapshot {
        uploadProfilePhotoCalled = true
        return uploadProfilePhotoResult!
    }

    func profilePhotoURL(baseURL: URL, relativePath: String) -> URL? {
        return URL(string: "https://example.com/photo")
    }
    
    var getContentPackagesResult: [ContentPackage] = []
    func getContentPackages(baseURL: URL) async throws -> [ContentPackage] {
        return getContentPackagesResult
    }
}

private enum MockError: Error {
    case missingStub(String)
}

private final class MockGameRealtimeClient: GameRealtimeClient {
    var status: RealtimeConnectionStatus = .disconnected
    var onStatusChanged: ((RealtimeConnectionStatus) -> Void)?
    var onSnapshot: ((RoomSnapshot) -> Void)?
    var onRoomStarted: ((RoomSnapshot) -> Void)?
    var onPlayerPrivateGameStateUpdated: ((PlayerPrivateGameState) -> Void)?

    var connectCalled = false
    func connect(baseURL: URL) async throws { connectCalled = true; status = .connected }
    
    var disconnectCalled = false
    func disconnect() async { disconnectCalled = true; status = .disconnected }

    var attachPlayerCalled = false
    var attachPlayerResult: RoomSnapshot?
    func attachPlayer(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot {
        attachPlayerCalled = true
        return attachPlayerResult!
    }

    var setReadyCalled = false
    var setReadyResult: RoomSnapshot?
    func setReady(roomCode: String, playerId: UUID, reconnectToken: String, isReady: Bool) async throws -> RoomSnapshot {
        setReadyCalled = true
        return setReadyResult!
    }

    var getRoomSnapshotCalled = false
    var getRoomSnapshotResult: RoomSnapshot?
    func getRoomSnapshot(roomCode: String) async throws -> RoomSnapshot {
        getRoomSnapshotCalled = true
        return getRoomSnapshotResult!
    }

    var submitPlayerSelectionResult: RoomSnapshot?
    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID) async throws -> RoomSnapshot {
        return submitPlayerSelectionResult!
    }

    var submitTextAnswerResult: RoomSnapshot?
    private(set) var textSubmissionIds: [UUID] = []
    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String) async throws -> RoomSnapshot {
        return submitTextAnswerResult!
    }
    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        textSubmissionIds.append(clientSubmissionId)
        return submitTextAnswerResult!
    }

    var submitTextAnswerVoteResult: RoomSnapshot?
    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID) async throws -> RoomSnapshot {
        return submitTextAnswerVoteResult!
    }
}

private final class MockPlayerSessionStorage: PlayerSessionStorageProtocol, @unchecked Sendable {
    var savedSession: (session: LocalPlayerSession, reconnectToken: String)?
    func saveSession(_ session: LocalPlayerSession, reconnectToken: String) throws {
        savedSession = (session, reconnectToken)
    }
    func loadSession() throws -> (session: LocalPlayerSession, reconnectToken: String)? {
        savedSession
    }
    func clearSession() throws {
        savedSession = nil
    }
}
