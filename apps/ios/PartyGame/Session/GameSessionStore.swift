import Foundation
import Observation
import UIKit

enum GameScreen: Equatable {
    case idle
    case hostSetup
    case joinSetup
    case profilePhoto
    case lobby
    case reconnecting
    case started
}

@MainActor
@Observable
final class GameSessionStore {
    private let configuration: ServerConfiguration
    private let api: RoomAPIClientProtocol
    private let realtime: GameRealtimeClient
    private let storage: PlayerSessionStorageProtocol
    private var accumulator = SnapshotAccumulator()
    private var reconnectToken: String?
    private var photoUploadTask: Task<Void, Never>?
    private var drawingUploadTask: Task<Void, Never>?
    private var privateStateRefreshTask: Task<Void, Never>?
    private var activeQuestionInstanceId: UUID?
    private var privateStateRefreshInFlightQuestionId: UUID?
    private var submissionIds: [String: UUID] = [:]
    private(set) var privateStateRefreshFailedQuestionId: UUID?

    private(set) var screen: GameScreen = .idle
    private(set) var session: LocalPlayerSession?
    private(set) var snapshot: RoomSnapshot?
    private(set) var privateGameState: PlayerPrivateGameState?
    private(set) var realtimeStatus: RealtimeConnectionStatus = .disconnected
    private(set) var isWorking = false
    private(set) var serverOffset: TimeInterval = 0
    private(set) var submittedQuestionInstanceIds: Set<UUID> = []
    private(set) var photoDraft: PhotoAnswerDraft?
    private(set) var photoUploadPhase: PhotoAnswerUploadPhase = .idle
    private(set) var selectedPhotoAnswerVoteId: UUID?
    private(set) var drawingDraft: DrawingAnswerDraft?
    private(set) var drawingUploadPhase: PhotoAnswerUploadPhase = .idle
    private(set) var selectedDrawingAnswerVoteId: UUID?
    private(set) var isPhotoCameraUnavailableFixture = false
    var errorMessage: String?

    convenience init(configuration: ServerConfiguration) {
        self.init(
            configuration: configuration,
            api: RoomAPIClient(),
            realtime: SignalRGameRealtimeClient(),
            storage: PlayerSessionStorage()
        )
    }

    init(
        configuration: ServerConfiguration,
        api: RoomAPIClientProtocol,
        realtime: GameRealtimeClient,
        storage: PlayerSessionStorageProtocol
    ) {
        self.configuration = configuration
        self.api = api
        self.realtime = realtime
        self.storage = storage

        realtime.onStatusChanged = { [weak self] status in
            self?.realtimeStatus = status
            if status == .reconnecting, self?.session != nil { self?.screen = .reconnecting }
        }
        realtime.onSnapshot = { [weak self] snapshot in self?.apply(snapshot) }
        realtime.onRoomStarted = { [weak self] snapshot in
            self?.apply(snapshot)
            self?.screen = .started
        }
        realtime.onPlayerPrivateGameStateUpdated = { [weak self] state in
            self?.applyPrivateGameState(state)
        }
        PhotoAnswerDraftStorage.cleanup()
    }

    func openDrawingCanvas() {
        guard let session, let questionId = snapshot?.game?.resolvedQuestionInstanceId,
              snapshot?.game?.stage == .collectingDrawingAnswers else { return }
        drawingDraft = DrawingAnswerDraftStorage.load(roomCode: session.roomCode, playerId: session.playerId, questionInstanceId: questionId)
            ?? DrawingAnswerDraft(roomCode: session.roomCode, playerId: session.playerId, questionInstanceId: questionId, canvas: DrawingCanvasState(), clientSubmissionId: nil, pngURL: nil, previewPNG: nil)
    }

    func updateDrawingCanvas(_ canvas: DrawingCanvasState) {
        guard var draft = drawingDraft else { return }
        draft.canvas = canvas; draft.pngURL = nil; draft.previewPNG = nil; draft.clientSubmissionId = nil
        drawingDraft = draft; try? DrawingAnswerDraftStorage.save(draft)
    }

    func closeDrawingCanvas() { }

    func previewDrawing() async {
        guard var draft = drawingDraft else { return }
        guard !draft.canvas.isEmpty else { errorMessage = String(localized: "drawing.error.empty"); return }
        drawingUploadPhase = .preparing; errorMessage = nil
        do {
            let png = try await DrawingRenderer().render(draft.canvas)
            guard drawingDraft?.questionInstanceId == draft.questionInstanceId else { return }
            let pngURL = try DrawingAnswerDraftStorage.savePNG(png, for: draft)
            draft.pngURL = pngURL; draft.previewPNG = png
            drawingDraft = draft; try DrawingAnswerDraftStorage.save(draft); drawingUploadPhase = .ready
        } catch {
            drawingUploadPhase = .failed(error.localizedDescription); errorMessage = error.localizedDescription
        }
    }

    func uploadDrawingAnswer() {
        guard drawingUploadTask == nil, let draft = drawingDraft, draft.pngURL != nil,
              snapshot?.game?.stage == .collectingDrawingAnswers, !photoActionsExpired else { return }
        drawingUploadTask = Task { [weak self] in
            await self?.performDrawingAnswerUpload(draft)
            self?.drawingUploadTask = nil
        }
    }

    func cancelDrawingUpload() { drawingUploadTask?.cancel(); drawingUploadTask = nil; if drawingDraft != nil { drawingUploadPhase = .ready } }

    func selectDrawingAnswerVote(_ id: UUID) { guard !photoActionsExpired else { return }; selectedDrawingAnswerVoteId = id }

    func submitSelectedDrawingAnswerVote() async {
        guard !isWorking, snapshot?.game?.stage == .collectingDrawingAnswerVotes, !photoActionsExpired,
              privateGameState?.hasSubmittedDrawingAnswerVote != true, let session, let reconnectToken,
              let questionId = snapshot?.game?.resolvedQuestionInstanceId, let drawingId = selectedDrawingAnswerVoteId else { return }
        isWorking = true; errorMessage = nil; defer { isWorking = false }
        do { try await realtime.submitDrawingAnswerVote(roomCode: session.roomCode, playerId: session.playerId, reconnectToken: reconnectToken, questionInstanceId: questionId, drawingAnswerId: drawingId, clientSubmissionId: submissionId(questionId, "drawing-vote")) }
        catch { errorMessage = Self.drawingAnswerMessage(for: error) }
    }

    var ownPlayer: RoomPlayer? {
        guard let playerId = session?.playerId else { return nil }
        return snapshot?.players.first { $0.id == playerId }
    }

    var baseURL: URL? { ServerConfiguration.validatedURL(from: session?.serverBaseURL ?? configuration.baseURL) }

    var realtimeDiagnosticState: String {
        switch realtimeStatus {
        case .disconnected: "Disconnected"
        case .connecting: "Connecting"
        case .connected: "Connected"
        case .reconnecting: "Reconnecting"
        case .failed: "Failed"
        }
    }

    var photoActionsExpired: Bool {
        guard let value = snapshot?.game?.stageEndsAtUtc,
              let end = ISO8601DateFormatter().date(from: value) else { return false }
        return Date().addingTimeInterval(serverOffset) >= end
    }

    func showHostSetup() { screen = .hostSetup; errorMessage = nil }
    func showJoinSetup() { screen = .joinSetup; errorMessage = nil }
    func showHome() { screen = .idle; errorMessage = nil }

    func fetchPackages() async throws -> [ContentPackage] {
        guard let baseURL = ServerConfiguration.validatedURL(from: configuration.baseURL) else { throw RoomAPIError.invalidRequest }
        return try await api.getContentPackages(baseURL: baseURL)
    }

    func createRoom(
        nickname: String,
        settings: RoomSettings,
        selectedPackageKeys: [String]?,
        enabledQuestionTypes: [String]? = nil
    ) async {
        guard validateNickname(nickname), settings.isValid, let baseURL = ServerConfiguration.validatedURL(from: configuration.baseURL) else {
            errorMessage = String(localized: "error.invalid_form")
            return
        }
        await performAccess {
            try await api.createRoom(
                baseURL: baseURL,
                request: CreateRoomRequest(
                    nickname: nickname.trimmingCharacters(in: .whitespacesAndNewlines),
                    settings: settings,
                    selectedPackageKeys: selectedPackageKeys,
                    enabledQuestionTypes: enabledQuestionTypes
                )
            )
        }
    }

    func joinRoom(roomCode: String, nickname: String) async {
        let code = Self.normalizedRoomCode(roomCode)
        guard code.count == 4, validateNickname(nickname), let baseURL = ServerConfiguration.validatedURL(from: configuration.baseURL) else {
            errorMessage = String(localized: "error.invalid_form")
            return
        }
        await performAccess {
            try await api.joinRoom(
                baseURL: baseURL,
                roomCode: code,
                request: JoinRoomRequest(nickname: nickname.trimmingCharacters(in: .whitespacesAndNewlines))
            )
        }
    }

    func uploadProfilePhoto(_ jpegData: Data) async {
        guard let session, let reconnectToken, let baseURL else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let updated = try await api.uploadProfilePhoto(
                baseURL: baseURL,
                session: session,
                reconnectToken: reconnectToken,
                jpegData: jpegData
            )
            apply(updated)
            screen = updated.phase == .started ? .started : .lobby
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func setReady(_ isReady: Bool) async {
        guard let session, let reconnectToken else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let updated = try await realtime.setReady(
                roomCode: session.roomCode,
                playerId: session.playerId,
                reconnectToken: reconnectToken,
                isReady: isReady
            )
            apply(updated)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func submitPlayerSelection(selectedPlayerId: UUID) async {
        guard let session, let reconnectToken, let questionId = snapshot?.game?.currentQuestion?.instanceId else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let updated = try await realtime.submitPlayerSelection(
                roomCode: session.roomCode,
                playerId: session.playerId,
                reconnectToken: reconnectToken,
                selectedPlayerId: selectedPlayerId,
                questionInstanceId: questionId,
                clientSubmissionId: submissionId(questionId, "player-selection")
            )
            submittedQuestionInstanceIds.insert(questionId)
            apply(updated)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func submitTextAnswer(text: String) async {
        guard let session, let reconnectToken, let questionId = snapshot?.game?.currentQuestion?.instanceId else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let updated = try await realtime.submitTextAnswer(
                roomCode: session.roomCode,
                playerId: session.playerId,
                reconnectToken: reconnectToken,
                text: text,
                questionInstanceId: questionId,
                clientSubmissionId: submissionId(questionId, "text-answer")
            )
            submittedQuestionInstanceIds.insert(questionId)
            apply(updated)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func submitTextAnswerVote(selectedAnswerId: UUID) async {
        guard let session, let reconnectToken, let questionId = snapshot?.game?.resolvedQuestionInstanceId else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let updated = try await realtime.submitTextAnswerVote(
                roomCode: session.roomCode,
                playerId: session.playerId,
                reconnectToken: reconnectToken,
                selectedAnswerId: selectedAnswerId,
                questionInstanceId: questionId,
                clientSubmissionId: submissionId(questionId, "text-vote")
            )
            apply(updated)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func preparePhotoAnswer(_ image: UIImage) async {
        guard let session, let questionId = snapshot?.game?.resolvedQuestionInstanceId,
              snapshot?.game?.stage == .collectingPhotoAnswers, !photoActionsExpired else { return }
        photoUploadPhase = .preparing
        errorMessage = nil
        do {
            let prepared = try await PhotoAnswerProcessor().prepare(image: image)
            guard snapshot?.game?.resolvedQuestionInstanceId == questionId else { return }
            if let old = photoDraft { PhotoAnswerDraftStorage.remove(old) }
            photoDraft = try PhotoAnswerDraftStorage.save(prepared, roomCode: session.roomCode, playerId: session.playerId,
                                                          questionInstanceId: questionId, submissionId: UUID())
            photoUploadPhase = .ready
        } catch {
            photoUploadPhase = .failed(error.localizedDescription)
            errorMessage = error.localizedDescription
        }
    }

    func discardPhotoAnswerDraft() {
        guard photoUploadTask == nil else { return }
        if let photoDraft { PhotoAnswerDraftStorage.remove(photoDraft) }
        photoDraft = nil
        photoUploadPhase = .idle
    }

    func uploadPhotoAnswer() {
        guard photoUploadTask == nil, snapshot?.game?.stage == .collectingPhotoAnswers,
              !photoActionsExpired, let draft = photoDraft else { return }
        photoUploadTask = Task { [weak self] in
            await self?.performPhotoAnswerUpload(draft)
            self?.photoUploadTask = nil
        }
    }

    func cancelPhotoAnswerUpload() {
        photoUploadTask?.cancel()
        photoUploadTask = nil
        if photoDraft != nil { photoUploadPhase = .ready }
    }

    func selectPhotoAnswerVote(_ id: UUID) {
        // The voting view exists only in CollectingPhotoAnswerVotes. Keeping the
        // local selection during a transient pause/reconnect lets the router restore it.
        guard !photoActionsExpired else { return }
        selectedPhotoAnswerVoteId = id
    }

    func submitSelectedPhotoAnswerVote() async {
        guard !isWorking, snapshot?.game?.stage == .collectingPhotoAnswerVotes,
              !photoActionsExpired,
              privateGameState?.hasSubmittedPhotoAnswerVote != true,
              let session, let reconnectToken, let questionId = snapshot?.game?.resolvedQuestionInstanceId,
              let photoAnswerId = selectedPhotoAnswerVoteId else { return }
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            try await realtime.submitPhotoAnswerVote(roomCode: session.roomCode, playerId: session.playerId,
                                                     reconnectToken: reconnectToken, questionInstanceId: questionId,
                                                     photoAnswerId: photoAnswerId, clientSubmissionId: submissionId(questionId, "photo-vote"))
        } catch { errorMessage = error.localizedDescription }
    }

    func restoreSession() async {
        guard !isUITesting else { return }
        guard session == nil else { return }
        do {
            guard let saved = try storage.loadSession(),
                  let baseURL = ServerConfiguration.validatedURL(from: saved.session.serverBaseURL) else { return }
            session = saved.session
            reconnectToken = saved.reconnectToken
            screen = .reconnecting
            let resumed = try await api.resume(baseURL: baseURL, session: saved.session, reconnectToken: saved.reconnectToken)
            apply(resumed.snapshot)
            applyPrivateGameState(resumed.privateState)
            try await connectAndAttach(baseURL: baseURL, session: saved.session, token: saved.reconnectToken)
            routeUsingSnapshot(resumed.snapshot)
        } catch let error as RoomAPIError where error.isInvalidSession {
            try? storage.clearSession()
            clearMemory()
            errorMessage = String(localized: "session.expired")
        } catch {
            screen = .reconnecting
            errorMessage = error.localizedDescription
        }
    }

    func retryConnection() async {
        guard !isUITesting else { return }
        guard let session, let reconnectToken, let baseURL else { return }
        do {
            try await connectAndAttach(baseURL: baseURL, session: session, token: reconnectToken)
            let resumed = try await api.resume(baseURL: baseURL, session: session, reconnectToken: reconnectToken)
            apply(resumed.snapshot)
            applyPrivateGameState(resumed.privateState)
            routeUsingSnapshot(resumed.snapshot)
            errorMessage = nil
        } catch {
            screen = .reconnecting
            errorMessage = error.localizedDescription
        }
    }

    func applicationBecameActive() async {
        guard !isUITesting else { return }
        guard session != nil, realtimeStatus != .connected else { return }
        await retryConnection()
    }

    func serverAddressChanged() async {
        guard !isUITesting else { return }
        guard let session, session.serverBaseURL != configuration.baseURL else { return }
        await realtime.disconnect()
        realtimeStatus = .disconnected
    }

    func forgetSession() async {
        await realtime.disconnect()
        do { try storage.clearSession() } catch { errorMessage = error.localizedDescription }
        clearMemory()
    }

    func showPhotoCapture() {
        guard snapshot?.phase == .lobby else { return }
        screen = .profilePhoto
    }

    func profilePhotoURL(for player: RoomPlayer) -> URL? {
        guard let path = player.profilePhotoUrl, let baseURL else { return nil }
        return api.profilePhotoURL(baseURL: baseURL, relativePath: path)
    }

    func mediaURL(_ relativePath: String?) -> URL? {
        guard let relativePath, let baseURL else { return nil }
        return api.profilePhotoURL(baseURL: baseURL, relativePath: relativePath)
    }

    func apply(_ candidate: RoomSnapshot) {
        // Fallback for server time synchronisation if timestampUtc is available in future, or just using current snapshot delivery as reference.
        // If we want a rough server time offset based on nothing, we can't do much without a timestamp from server. 
        // We'll leave the variable updated if we had a way.
        
        guard accumulator.accept(candidate) else { return }
        let enteredDrawingAnswerCollection = candidate.game?.stage == .collectingDrawingAnswers &&
            snapshot?.game?.stage != .collectingDrawingAnswers
        snapshot = candidate
        let nextQuestion = candidate.game?.resolvedQuestionInstanceId
        let questionChanged = nextQuestion != activeQuestionInstanceId
        if questionChanged {
            submissionIds = [:]
            photoUploadTask?.cancel()
            photoUploadTask = nil
            if let photoDraft { PhotoAnswerDraftStorage.remove(photoDraft) }
            photoDraft = nil
            photoUploadPhase = .idle
            selectedPhotoAnswerVoteId = nil
            drawingUploadTask?.cancel()
            drawingUploadTask = nil
            if let drawingDraft { DrawingAnswerDraftStorage.remove(drawingDraft) }
            drawingDraft = nil
            drawingUploadPhase = .idle
            selectedDrawingAnswerVoteId = nil
            privateGameState = nil
            privateStateRefreshFailedQuestionId = nil
        }
        activeQuestionInstanceId = nextQuestion
        if !isUITesting, (questionChanged || enteredDrawingAnswerCollection), nextQuestion != nil {
            // Drawing eligibility is created with the transition to this stage.
            // A private state fetched during the preceding intro has the same
            // question id but predates that eligibility list, so it cannot be
            // used to decide that a player is waiting rather than eligible.
            if enteredDrawingAnswerCollection {
                privateGameState = nil
                privateStateRefreshFailedQuestionId = nil
            }
            // A public stage transition never carries player-private fields. Refreshing through
            // the existing resume contract prevents a new answer/vote screen from waiting for a
            // private event that belonged to the preceding question.
            privateStateRefreshTask?.cancel()
            privateStateRefreshTask = Task { [weak self] in
                guard !Task.isCancelled else { return }
                await self?.refreshPrivateStateForActiveQuestion(nextQuestion)
            }
        }
        
        // Reset submitted questions if the round/question changes
        if let game = candidate.game, let currentQ = game.currentQuestion {
            if !submittedQuestionInstanceIds.contains(currentQ.instanceId) {
                // Not submitted yet for this question instance
            }
        }
        
        if candidate.phase == .started { screen = .started }
        else if screen == .reconnecting { screen = ownPlayer?.hasProfilePhoto == true ? .lobby : .profilePhoto }
    }

    static func normalizedRoomCode(_ input: String) -> String {
        let allowed = Set("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
        return String(input.uppercased().filter { allowed.contains($0) }.prefix(4))
    }

    private var isUITesting = false

    func configureUITestScenario(arguments: [String]) {
        #if DEBUG
        if arguments.contains("-uiTestingHome") {
            isUITesting = true
            screen = .idle
            return
        }
        let photoArgument = arguments.first { $0.hasPrefix("-uiTestingPhoto") }
        let drawingArgument = arguments.first { $0.hasPrefix("-uiTestingDrawing") }
        let gameScreenArgument = arguments.first { $0.hasPrefix("-uiTestingGameScreen") }
        guard arguments.contains("-uiTestingLobby") || arguments.contains("-uiTestingStarted") || photoArgument != nil || drawingArgument != nil || gameScreenArgument != nil else { return }
        if let gameScreenArgument {
            configureGameScreenUITestScenario(gameScreenArgument)
            return
        }
        if let drawingArgument {
            configureDrawingUITestScenario(drawingArgument)
            return
        }
        isUITesting = true
        isPhotoCameraUnavailableFixture = photoArgument == "-uiTestingPhotoCameraUnavailable"
        let playerId = UUID(uuidString: "0DC81D35-C68D-47C6-AEBB-5E86407A1BB0")!
        session = LocalPlayerSession(roomCode: "ABCD", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: configuration.baseURL)
        reconnectToken = "ui-test-token"
        let phase: RoomPhase = arguments.contains("-uiTestingStarted") || photoArgument != nil ? .started : .lobby
        let questionId = UUID(uuidString: "30000000-0000-0000-0000-000000000001")!
        let ownPhotoId = UUID(uuidString: "40000000-0000-0000-0000-000000000001")!
        let otherPhotoId = UUID(uuidString: "40000000-0000-0000-0000-000000000002")!
        let photoStage: GameStage? = {
            switch photoArgument {
            case "-uiTestingPhotoReveal": .revealingPhotoAnswers
            case "-uiTestingPhotoVoting", "-uiTestingPhotoVoteWaiting": .collectingPhotoAnswerVotes
            case "-uiTestingPhotoResults", "-uiTestingPhotoZero", "-uiTestingPhotoOne", "-uiTestingPhotoTie": .showingPhotoAnswerResults
            case "-uiTestingPhotoPaused": .pausedForDisplay
            case .some: .collectingPhotoAnswers
            case .none: nil
            }
        }()
        let anonymous = [
            AnonymousPhotoAnswer(photoAnswerId: ownPhotoId, displayPhotoUrl: nil, thumbnailPhotoUrl: nil, displayOrder: 0, width: 900, height: 1_600),
            AnonymousPhotoAnswer(photoAnswerId: otherPhotoId, displayPhotoUrl: nil, thumbnailPhotoUrl: nil, displayOrder: 1, width: 1_600, height: 900),
        ]
        let voter = PhotoAnswerResultVoter(playerId: playerId, nickname: "Ola", profilePhotoUrl: nil, pointsAwarded: 100)
        let resultOne = PhotoAnswerResultOption(photoAnswerId: ownPhotoId, displayPhotoUrl: nil, thumbnailPhotoUrl: nil,
            width: 900, height: 1_600, authorPlayerId: playerId, authorNickname: "Ola", authorPhotoUrl: nil,
            voteCount: photoArgument == "-uiTestingPhotoOne" ? 0 : 1, isTopResult: photoArgument != "-uiTestingPhotoOne", voters: photoArgument == "-uiTestingPhotoOne" ? [] : [voter])
        let resultTwo = PhotoAnswerResultOption(photoAnswerId: otherPhotoId, displayPhotoUrl: nil, thumbnailPhotoUrl: nil,
            width: 1_600, height: 900, authorPlayerId: UUID(uuidString: "38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA")!,
            authorNickname: "Jan", authorPhotoUrl: nil, voteCount: 1, isTopResult: true, voters: [voter])
        let resultOptions: [PhotoAnswerResultOption]? = photoArgument == "-uiTestingPhotoZero" ? [] :
            (photoArgument == "-uiTestingPhotoOne" ? [resultOne] : [resultOne, resultTwo])
        let photoResults = PhotoAnswerResults(questionInstanceId: questionId, submittedPlayers: photoArgument == "-uiTestingPhotoZero" ? 0 : 2,
            requiredPlayers: 2, votedPlayers: 1, requiredVoters: 2, missingSubmissionPlayers: 0, missingVotePlayers: 1,
            highestVoteCount: 1, options: photoStage == .showingPhotoAnswerResults ? resultOptions : nil,
            anonymousOptions: photoStage == .revealingPhotoAnswers || photoStage == .collectingPhotoAnswerVotes ? anonymous : nil)
        let fixture = RoomSnapshot(
            roomCode: "ABCD", phase: phase, stateVersion: 7, displayConnected: true,
            minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: [
                RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: false, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0),
                RoomPlayer(id: UUID(uuidString: "38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA")!, nickname: "Jan", isHost: false, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)
            ], createdAtUtc: "2026-07-20T12:00:00Z", startedAtUtc: phase == .started ? "2026-07-20T12:01:00Z" : nil,
            game: phase == .started ? GameSnapshot(
                stage: photoStage ?? .categoryIntro, currentRoundNumber: 1, totalRounds: 1,
                currentQuestionNumber: 1, questionsInCurrentRound: 4,
                stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil,
                pausedRemainingMilliseconds: nil, scores: [], categories: [GameCategorySnapshot(id: UUID(), name: "Zabawa", backgroundHexColor: "#241146")],
                currentQuestion: photoStage == nil ? nil : GameQuestionSnapshot(instanceId: questionId, categoryId: UUID(),
                    questionText: LocalizedText(defaultText: "Zrób zdjęcie czegoś czerwonego", translations: nil), requiredAnswerType: "PhotoAnswer"),
                playerSelectionResults: nil,
                roundSummary: nil, textAnswerResults: nil
                , photoAnswerResults: photoStage == nil ? nil : photoResults
            ) : nil
        )
        apply(fixture)
        if photoStage != nil {
            privateGameState = PlayerPrivateGameState(playerId: playerId, questionInstanceId: questionId,
                hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false,
                hasSubmittedPhotoAnswer: photoArgument == "-uiTestingPhotoWaiting" || photoArgument == "-uiTestingPhotoVoteWaiting",
                ownPhotoAnswerId: ownPhotoId, hasSubmittedPhotoAnswerVote: photoArgument == "-uiTestingPhotoVoteWaiting")
            if photoArgument == "-uiTestingPhotoPreview" || photoArgument == "-uiTestingPhotoUpload" {
                let format = UIGraphicsImageRendererFormat(); format.scale = 1
                let image = UIGraphicsImageRenderer(size: CGSize(width: 640, height: 480), format: format).image { context in
                    UIColor.systemPink.setFill(); context.fill(CGRect(x: 0, y: 0, width: 640, height: 480))
                }
                if let data = image.jpegData(compressionQuality: 0.8) {
                    let prepared = PreparedPhotoAnswer(jpegData: data, width: 640, height: 480, byteCount: data.count)
                    photoDraft = try? PhotoAnswerDraftStorage.save(prepared, roomCode: "ABCD", playerId: playerId,
                        questionInstanceId: questionId, submissionId: UUID())
                    photoUploadPhase = photoArgument == "-uiTestingPhotoUpload" ? .uploading(0.42) : .ready
                }
            }
        }
        screen = phase == .started ? .started : .lobby
        #endif
    }

    #if DEBUG
    private func configureGameScreenUITestScenario(_ argument: String) {
        isUITesting = true
        let ola = UUID(uuidString: "0DC81D35-C68D-47C6-AEBB-5E86407A1BB0")!
        let jan = UUID(uuidString: "38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA")!
        let ewa = UUID(uuidString: "71A8C49F-1A2B-418F-A5CD-7D47C9BC9280")!
        let questionId = UUID(uuidString: "30000000-0000-0000-0000-000000000001")!
        session = LocalPlayerSession(roomCode: "ABCD", playerId: ola, nickname: "Ola", isHost: true, serverBaseURL: configuration.baseURL)
        reconnectToken = "ui-test-token"
        let players = [
            RoomPlayer(id: ola, nickname: "Ola", isHost: true, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0),
            RoomPlayer(id: jan, nickname: "Jan", isHost: false, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 500),
            RoomPlayer(id: ewa, nickname: "Ewa", isHost: false, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0)
        ]
        let rankings = [
            RankingEntry(playerId: jan, nickname: "Jan", profilePhotoUrl: nil, score: 500, position: 1),
            RankingEntry(playerId: ola, nickname: "Ola", profilePhotoUrl: nil, score: 0, position: 2),
            RankingEntry(playerId: ewa, nickname: "Ewa", profilePhotoUrl: nil, score: 0, position: 2)
        ]
        let results = PlayerSelectionResults(questionInstanceId: questionId, answeredPlayers: 3, requiredPlayers: 3,
            missingPlayers: 0, highestVoteCount: 2, options: [
                PlayerSelectionResultOption(selectedPlayerId: jan, selectedPlayerNickname: "Jan", selectedPlayerPhotoUrl: nil,
                    voteCount: 2, isTopResult: true, voters: [
                        ResultVoter(playerId: ola, nickname: "Ola", profilePhotoUrl: nil, pointsAwarded: 500),
                        ResultVoter(playerId: ewa, nickname: "Ewa", profilePhotoUrl: nil, pointsAwarded: 0)
                    ])
            ])
        let stage: GameStage = switch argument {
        case "-uiTestingGameScreenResults": .showingQuestionResults
        case "-uiTestingGameScreenRoundSummary": .roundSummary
        case "-uiTestingGameScreenCompleted": .completed
        default: .collectingPlayerSelections
        }
        let game = GameSnapshot(stage: stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: nil, pausedStage: nil,
            pausedRemainingMilliseconds: nil, scores: [], categories: [GameCategorySnapshot(id: UUID(), name: "Zabawa", backgroundHexColor: "#241146")],
            currentQuestion: GameQuestionSnapshot(instanceId: questionId, categoryId: UUID(),
                questionText: LocalizedText(defaultText: "Wybierz osobę, która rozbawiła Cię najbardziej.", translations: nil), requiredAnswerType: "PlayerSelection"),
            playerSelectionResults: stage == .showingQuestionResults ? results : nil,
            roundSummary: stage == .roundSummary || stage == .completed ? RoundSummarySnapshot(roundNumber: 1, rankings: rankings) : nil,
            textAnswerResults: nil, ranking: rankings)
        apply(RoomSnapshot(roomCode: "ABCD", phase: .started, stateVersion: 7,
            displayConnected: true, minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: players, createdAtUtc: "2026-08-12T12:00:00Z", startedAtUtc: "2026-08-12T12:01:00Z", game: game))
        screen = .started
    }
    #endif

    #if DEBUG
    private func configureDrawingUITestScenario(_ argument: String) {
        isUITesting = true
        let playerId = UUID(uuidString: "0DC81D35-C68D-47C6-AEBB-5E86407A1BB0")!
        let otherId = UUID(uuidString: "38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA")!
        let questionId = UUID(uuidString: "30000000-0000-0000-0000-000000000001")!
        let ownDrawingId = UUID(uuidString: "50000000-0000-0000-0000-000000000001")!
        let otherDrawingId = UUID(uuidString: "50000000-0000-0000-0000-000000000002")!
        let cleanDraft = DrawingAnswerDraft(roomCode: "ABCD", playerId: playerId, questionInstanceId: questionId,
            canvas: DrawingCanvasState(), clientSubmissionId: nil, pngURL: nil, previewPNG: nil)
        DrawingAnswerDraftStorage.remove(cleanDraft)
        drawingDraft = nil
        drawingUploadPhase = .idle
        let stage: GameStage = switch argument {
        case "-uiTestingDrawingReveal": .revealingDrawingAnswers
        case "-uiTestingDrawingVoting", "-uiTestingDrawingVoteWaiting": .collectingDrawingAnswerVotes
        case "-uiTestingDrawingResults", "-uiTestingDrawingZero", "-uiTestingDrawingOne", "-uiTestingDrawingTie": .showingDrawingAnswerResults
        case "-uiTestingDrawingPaused": .pausedForDisplay
        default: .collectingDrawingAnswers
        }
        let anonymous = [
            AnonymousDrawingOption(drawingAnswerId: ownDrawingId, displayDrawingUrl: nil, thumbnailDrawingUrl: nil, displayOrder: 0, revealOrder: 0, width: 1024, height: 1024),
            AnonymousDrawingOption(drawingAnswerId: otherDrawingId, displayDrawingUrl: nil, thumbnailDrawingUrl: nil, displayOrder: 1, revealOrder: 1, width: 1024, height: 1024),
        ]
        let voter = DrawingAnswerResultVoter(playerId: otherId, nickname: "Jan", profilePhotoUrl: nil, pointsAwarded: 100)
        let first = DrawingAnswerResultOption(drawingAnswerId: ownDrawingId, displayDrawingUrl: nil, thumbnailDrawingUrl: nil,
            width: 1024, height: 1024, authorPlayerId: playerId, authorNickname: "Ola", authorPhotoUrl: nil,
            voteCount: argument == "-uiTestingDrawingOne" ? 0 : 1, isTopResult: argument != "-uiTestingDrawingOne", voters: argument == "-uiTestingDrawingOne" ? [] : [voter])
        let second = DrawingAnswerResultOption(drawingAnswerId: otherDrawingId, displayDrawingUrl: nil, thumbnailDrawingUrl: nil,
            width: 1024, height: 1024, authorPlayerId: otherId, authorNickname: "Jan", authorPhotoUrl: nil,
            voteCount: 1, isTopResult: true, voters: [DrawingAnswerResultVoter(playerId: playerId, nickname: "Ola", profilePhotoUrl: nil, pointsAwarded: 100)])
        let resultOptions: [DrawingAnswerResultOption]? = argument == "-uiTestingDrawingZero" ? [] :
            (argument == "-uiTestingDrawingOne" ? [first] : [first, second])
        let drawingResults = DrawingAnswerResultsSnapshot(questionInstanceId: questionId,
            submittedDrawingAnswers: argument == "-uiTestingDrawingZero" ? 0 : 2, requiredDrawingAnswers: 2,
            submittedDrawingAnswerPlayerIds: nil, votedPlayers: 1, requiredVoters: 2, highestVoteCount: 1,
            options: stage == .showingDrawingAnswerResults ? resultOptions : nil,
            anonymousOptions: stage == .revealingDrawingAnswers || stage == .collectingDrawingAnswerVotes ? anonymous : nil)
        session = LocalPlayerSession(roomCode: "ABCD", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: configuration.baseURL)
        reconnectToken = "ui-test-token"
        let game = GameSnapshot(stage: stage, currentRoundNumber: 1, totalRounds: 1, currentQuestionNumber: 1,
            questionsInCurrentRound: 4, stageEndsAtUtc: nil, pausedAtUtc: stage == .pausedForDisplay ? "2026-07-21T12:00:00Z" : nil,
            pausedStage: stage == .pausedForDisplay ? .collectingDrawingAnswers : nil, pausedRemainingMilliseconds: stage == .pausedForDisplay ? 30_000 : nil,
            scores: [], categories: [GameCategorySnapshot(id: UUID(), name: "Zabawa", backgroundHexColor: "#241146")],
            currentQuestion: GameQuestionSnapshot(instanceId: questionId, categoryId: UUID(),
                questionText: LocalizedText(defaultText: "Narysuj coś zabawnego", translations: nil), requiredAnswerType: "DrawingAnswer"),
            playerSelectionResults: nil, roundSummary: nil, textAnswerResults: nil, photoAnswerResults: nil,
            drawingAnswerResults: drawingResults)
        apply(RoomSnapshot(roomCode: "ABCD", phase: .started, stateVersion: 7, displayConnected: true,
            minimumPlayers: 3, maximumPlayers: 8, canStart: false, settings: RoomSettings(),
            players: [
                RoomPlayer(id: playerId, nickname: "Ola", isHost: true, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0),
                RoomPlayer(id: otherId, nickname: "Jan", isHost: false, isReady: true, isConnected: true, hasProfilePhoto: true, profilePhotoUrl: nil, score: 0),
            ], createdAtUtc: "2026-07-21T12:00:00Z", startedAtUtc: "2026-07-21T12:01:00Z", game: game))
        privateGameState = PlayerPrivateGameState(playerId: playerId, questionInstanceId: questionId,
            hasSubmittedTextAnswer: false, ownTextAnswerId: nil, hasSubmittedTextAnswerVote: false,
            hasSubmittedDrawingAnswer: argument == "-uiTestingDrawingWaiting" || argument == "-uiTestingDrawingVoteWaiting",
            ownDrawingAnswerId: ownDrawingId, hasSubmittedDrawingAnswerVote: argument == "-uiTestingDrawingVoteWaiting",
            isEligibleForDrawingAnswer: stage == .collectingDrawingAnswers)
        if ["-uiTestingDrawingPreview", "-uiTestingDrawingUpload", "-uiTestingDrawingRetry"].contains(argument) {
            var canvas = DrawingCanvasState()
            canvas.complete([DrawingPoint(x: 0.15, y: 0.15), DrawingPoint(x: 0.85, y: 0.85)])
            let format = UIGraphicsImageRendererFormat(); format.scale = 1; format.opaque = true
            let png = UIGraphicsImageRenderer(size: DrawingRenderer.logicalSize, format: format).image { context in
                UIColor.white.setFill(); context.fill(CGRect(origin: .zero, size: DrawingRenderer.logicalSize))
                UIColor.black.setStroke()
            }.pngData()
            drawingDraft = DrawingAnswerDraft(roomCode: "ABCD", playerId: playerId, questionInstanceId: questionId,
                canvas: canvas, clientSubmissionId: UUID(), pngURL: nil, previewPNG: png)
            drawingUploadPhase = argument == "-uiTestingDrawingUpload" ? .uploading(0.42) :
                (argument == "-uiTestingDrawingRetry" ? .failed("Błąd") : .ready)
        }
        screen = .started
    }
    #endif

    private func performAccess(_ operation: () async throws -> RoomAccessResponse) async {
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            let response = try await operation()
            let nickname = response.snapshot.players.first { $0.id == response.playerId }?.nickname ?? ""
            let local = LocalPlayerSession(
                roomCode: response.roomCode,
                playerId: response.playerId,
                nickname: nickname,
                isHost: response.snapshot.players.first { $0.id == response.playerId }?.isHost == true,
                serverBaseURL: configuration.baseURL
            )
            try storage.saveSession(local, reconnectToken: response.reconnectToken)
            session = local
            reconnectToken = response.reconnectToken
            apply(response.snapshot)
            applyPrivateGameState(response.privateState)
            guard let baseURL = ServerConfiguration.validatedURL(from: local.serverBaseURL) else { throw RoomAPIError.invalidRequest }
            try await connectAndAttach(baseURL: baseURL, session: local, token: response.reconnectToken)
            screen = .profilePhoto
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func connectAndAttach(baseURL: URL, session: LocalPlayerSession, token: String) async throws {
        try await realtime.connect(baseURL: baseURL)
        realtimeStatus = realtime.status
        let attached = try await realtime.attachPlayer(
            roomCode: session.roomCode,
            playerId: session.playerId,
            reconnectToken: token
        )
        apply(attached)
    }

    private func routeUsingSnapshot(_ snapshot: RoomSnapshot) {
        if snapshot.phase == .started { screen = .started }
        else { screen = ownPlayer?.hasProfilePhoto == true ? .lobby : .profilePhoto }
    }

    private func validateNickname(_ value: String) -> Bool {
        (2 ... 20).contains(value.trimmingCharacters(in: .whitespacesAndNewlines).count)
    }

    private func clearMemory() {
        photoUploadTask?.cancel()
        if let photoDraft { PhotoAnswerDraftStorage.remove(photoDraft) }
        session = nil
        reconnectToken = nil
        accumulator = SnapshotAccumulator()
        snapshot = nil
        privateGameState = nil
        photoDraft = nil
        photoUploadPhase = .idle
        selectedPhotoAnswerVoteId = nil
        drawingUploadTask?.cancel()
        privateStateRefreshTask?.cancel()
        privateStateRefreshTask = nil
        if let drawingDraft { DrawingAnswerDraftStorage.remove(drawingDraft) }
        drawingDraft = nil
        drawingUploadPhase = .idle
        selectedDrawingAnswerVoteId = nil
        activeQuestionInstanceId = nil
        submissionIds = [:]
        realtimeStatus = .disconnected
        screen = .idle
    }

    private func submissionId(_ questionId: UUID, _ action: String) -> UUID {
        let key = "\(questionId.uuidString)|\(action)"
        if let existing = submissionIds[key] { return existing }
        let created = UUID()
        submissionIds[key] = created
        return created
    }

    private func performPhotoAnswerUpload(_ draft: PhotoAnswerDraft) async {
        guard let session, let reconnectToken, let baseURL,
              snapshot?.game?.stage == .collectingPhotoAnswers,
              !photoActionsExpired,
              snapshot?.game?.resolvedQuestionInstanceId == draft.questionInstanceId,
              privateGameState?.hasSubmittedPhotoAnswer != true else { return }
        do {
            let data = try await Task.detached { try Data(contentsOf: draft.fileURL) }.value
            photoUploadPhase = .uploading(0)
            let response = try await api.uploadPhotoAnswer(
                baseURL: baseURL, session: session, reconnectToken: reconnectToken,
                questionInstanceId: draft.questionInstanceId, clientSubmissionId: draft.clientSubmissionId,
                jpegData: data
            ) { [weak self] value in
                Task { @MainActor in
                    guard let self, self.photoDraft?.clientSubmissionId == draft.clientSubmissionId else { return }
                    self.photoUploadPhase = value >= 1 ? .serverProcessing : .uploading(value)
                }
            }
            guard snapshot?.game?.resolvedQuestionInstanceId == draft.questionInstanceId else { return }
            apply(response.roomSnapshot)
            applyPrivateGameState(response.playerPrivateGameState)
            photoUploadPhase = .saved
            PhotoAnswerDraftStorage.remove(draft)
            photoDraft = PhotoAnswerDraft(roomCode: draft.roomCode, playerId: draft.playerId,
                                          questionInstanceId: draft.questionInstanceId,
                                          clientSubmissionId: draft.clientSubmissionId,
                                          fileURL: draft.fileURL, previewJPEG: draft.previewJPEG,
                                          width: draft.width, height: draft.height, byteCount: draft.byteCount)
        } catch is CancellationError {
            photoUploadPhase = .ready
        } catch {
            if case let RoomAPIError.http(_, problem) = error,
               let code = problem?.code,
               ["photo_answer_already_submitted", "photo_answer_not_active", "photo_answer_time_expired"].contains(code) {
                await refreshPrivateStateAfterStaleResponse()
            }
            let message = Self.photoAnswerMessage(for: error)
            photoUploadPhase = .failed(message)
            errorMessage = message
        }
    }

    private func performDrawingAnswerUpload(_ originalDraft: DrawingAnswerDraft) async {
        guard let session, let reconnectToken, let baseURL, let pngURL = originalDraft.pngURL,
              snapshot?.game?.stage == .collectingDrawingAnswers, !photoActionsExpired,
              snapshot?.game?.resolvedQuestionInstanceId == originalDraft.questionInstanceId,
              privateGameState?.hasSubmittedDrawingAnswer != true else { return }
        var draft = originalDraft
        if draft.clientSubmissionId == nil { draft.clientSubmissionId = UUID(); drawingDraft = draft; try? DrawingAnswerDraftStorage.save(draft) }
        guard let clientSubmissionId = draft.clientSubmissionId else { return }
        do {
            let data = try await Task.detached { try Data(contentsOf: pngURL) }.value
            drawingUploadPhase = .uploading(0)
            let response = try await api.uploadDrawingAnswer(baseURL: baseURL, session: session, reconnectToken: reconnectToken,
                                                              questionInstanceId: draft.questionInstanceId, clientSubmissionId: clientSubmissionId, pngData: data) { [weak self] value in
                Task { @MainActor in
                    guard self?.drawingDraft?.clientSubmissionId == clientSubmissionId else { return }
                    self?.drawingUploadPhase = value >= 1 ? .serverProcessing : .uploading(value)
                }
            }
            guard snapshot?.game?.resolvedQuestionInstanceId == draft.questionInstanceId else { return }
            apply(response.roomSnapshot); applyPrivateGameState(response.playerPrivateGameState)
            drawingUploadPhase = .saved
            DrawingAnswerDraftStorage.remove(draft)
            drawingDraft = DrawingAnswerDraft(roomCode: draft.roomCode, playerId: draft.playerId, questionInstanceId: draft.questionInstanceId,
                                               canvas: draft.canvas, clientSubmissionId: nil, pngURL: nil, previewPNG: draft.previewPNG)
        } catch is CancellationError { drawingUploadPhase = .ready }
        catch {
            if case let RoomAPIError.http(_, problem) = error,
               ["drawing_answer_already_submitted", "drawing_answer_not_active", "drawing_answer_time_expired"].contains(problem?.code ?? "") {
                await refreshPrivateStateAfterStaleResponse()
            }
            let message = Self.drawingAnswerMessage(for: error)
            drawingUploadPhase = .failed(message); errorMessage = message
        }
    }

    private func refreshPrivateStateAfterStaleResponse() async {
        await refreshPrivateStateForActiveQuestion(snapshot?.game?.resolvedQuestionInstanceId)
    }

    func refreshPrivateStateForActiveQuestion(_ expectedQuestionId: UUID?) async {
        guard let expectedQuestionId else { return }
        guard privateStateRefreshInFlightQuestionId != expectedQuestionId else { return }
        privateStateRefreshInFlightQuestionId = expectedQuestionId
        defer {
            if privateStateRefreshInFlightQuestionId == expectedQuestionId {
                privateStateRefreshInFlightQuestionId = nil
            }
        }
        // A room-stage event and its player-private counterpart can cross on a
        // reconnect. Retry the bounded resume read only while the same question
        // is still active; a response for Q1 must never populate Q2.
        for attempt in 0 ..< 5 {
            guard !Task.isCancelled,
                  snapshot?.game?.resolvedQuestionInstanceId == expectedQuestionId,
                  let session, let reconnectToken, let baseURL else { return }
            do {
                let resumed = try await api.resume(baseURL: baseURL, session: session, reconnectToken: reconnectToken)
                guard !Task.isCancelled else { return }
                // The public SignalR snapshot that started this refresh stays authoritative.
                // Applying the auxiliary resume snapshot here can move a just-entered
                // answer screen back to an older public representation of the question.
                guard snapshot?.game?.resolvedQuestionInstanceId == expectedQuestionId else { return }
                if resumed.privateState.questionInstanceId == expectedQuestionId {
                    applyPrivateGameState(resumed.privateState)
                    privateStateRefreshFailedQuestionId = nil
                    return
                }
            } catch is CancellationError {
                return
            } catch {
                if attempt < 4 { try? await Task.sleep(for: .milliseconds(500)) }
                continue
            }
            if attempt < 4 { try? await Task.sleep(for: .milliseconds(500)) }
        }
        guard !Task.isCancelled,
              snapshot?.game?.resolvedQuestionInstanceId == expectedQuestionId else { return }
        privateStateRefreshFailedQuestionId = expectedQuestionId
    }

    /// Ends asynchronous work owned by the store. App lifecycle code may use this when
    /// replacing a session; tests use it to guarantee that callbacks cannot escape tearDown.
    func shutdown() async {
        let photoTask = photoUploadTask
        let drawingTask = drawingUploadTask
        let privateTask = privateStateRefreshTask
        photoTask?.cancel()
        drawingTask?.cancel()
        privateTask?.cancel()
        _ = await photoTask?.result
        _ = await drawingTask?.result
        _ = await privateTask?.result
        photoUploadTask = nil
        drawingUploadTask = nil
        privateStateRefreshTask = nil
        realtime.onStatusChanged = nil
        realtime.onSnapshot = nil
        realtime.onRoomStarted = nil
        realtime.onPlayerPrivateGameStateUpdated = nil
        await realtime.disconnect()
    }

    func applyPrivateGameState(_ candidate: PlayerPrivateGameState) {
        guard candidate.playerId == session?.playerId else { return }
        let currentQuestion = snapshot?.game?.resolvedQuestionInstanceId
        guard candidate.questionInstanceId == currentQuestion else { return }
        if let current = privateGameState, current.questionInstanceId == candidate.questionInstanceId {
            privateGameState = PlayerPrivateGameState(
                playerId: candidate.playerId, questionInstanceId: candidate.questionInstanceId,
                hasSubmittedTextAnswer: current.hasSubmittedTextAnswer || candidate.hasSubmittedTextAnswer,
                ownTextAnswerId: candidate.ownTextAnswerId ?? current.ownTextAnswerId,
                hasSubmittedTextAnswerVote: current.hasSubmittedTextAnswerVote || candidate.hasSubmittedTextAnswerVote,
                isEligibleForTextAnswerVote: candidate.isEligibleForTextAnswerVote,
                hasSubmittedPhotoAnswer: current.hasSubmittedPhotoAnswer || candidate.hasSubmittedPhotoAnswer,
                ownPhotoAnswerId: candidate.ownPhotoAnswerId ?? current.ownPhotoAnswerId,
                hasSubmittedPhotoAnswerVote: current.hasSubmittedPhotoAnswerVote || candidate.hasSubmittedPhotoAnswerVote,
                hasSubmittedDrawingAnswer: current.hasSubmittedDrawingAnswer || candidate.hasSubmittedDrawingAnswer,
                ownDrawingAnswerId: candidate.ownDrawingAnswerId ?? current.ownDrawingAnswerId,
                hasSubmittedDrawingAnswerVote: current.hasSubmittedDrawingAnswerVote || candidate.hasSubmittedDrawingAnswerVote,
                isEligibleForDrawingAnswer: candidate.isEligibleForDrawingAnswer
            )
        } else { privateGameState = candidate }
    }

    private static func photoAnswerMessage(for error: Error) -> String {
        guard case let RoomAPIError.http(_, problem) = error else { return error.localizedDescription }
        switch problem?.code {
        case "photo_answer_time_expired", "photo_answer_not_active": return String(localized: "photoAnswer.error.expired")
        case "photo_answer_already_submitted": return String(localized: "photoAnswer.error.alreadySubmitted")
        case "photo_answer_player_not_eligible": return String(localized: "photoAnswer.error.notEligible")
        case "photo_answer_file_too_large": return String(localized: "photoAnswer.error.fileTooLarge")
        case "photo_answer_dimensions_too_small": return String(localized: "photoAnswer.error.dimensionsSmall")
        case "photo_answer_dimensions_too_large": return String(localized: "photoAnswer.error.dimensionsLarge")
        case "photo_answer_invalid_content_type", "photo_answer_invalid_image", "photo_answer_file_empty", "photo_answer_file_missing":
            return String(localized: "photoAnswer.error.invalidImage")
        case "photo_answer_storage_failed": return String(localized: "photoAnswer.error.storage")
        default: return problem?.userMessage ?? error.localizedDescription
        }
    }

    private static func drawingAnswerMessage(for error: Error) -> String {
        guard case let RoomAPIError.http(_, problem) = error else { return error.localizedDescription }
        switch problem?.code {
        case "drawing_answer_blank": return String(localized: "drawing.error.empty")
        case "drawing_answer_time_expired", "drawing_answer_not_active": return String(localized: "drawing.error.expired")
        case "drawing_answer_already_submitted": return String(localized: "drawing.error.alreadySubmitted")
        case "drawing_answer_file_too_large": return String(localized: "drawing.error.fileTooLarge")
        case "drawing_answer_invalid_content_type", "drawing_answer_invalid_image", "drawing_answer_file_empty", "drawing_answer_file_missing": return String(localized: "drawing.error.invalidImage")
        case "drawing_answer_storage_failed": return String(localized: "drawing.error.storage")
        default: return problem?.userMessage ?? error.localizedDescription
        }
    }
}
