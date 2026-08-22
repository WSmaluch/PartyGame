import Foundation
import SignalRClient

enum RealtimeConnectionStatus: Equatable, Sendable {
    case disconnected
    case connecting
    case connected
    case reconnecting
    case failed(String)
}

@MainActor
protocol GameRealtimeClient: AnyObject {
    var status: RealtimeConnectionStatus { get }
    var onStatusChanged: ((RealtimeConnectionStatus) -> Void)? { get set }
    var onSnapshot: ((RoomSnapshot) -> Void)? { get set }
    var onRoomStarted: ((RoomSnapshot) -> Void)? { get set }
    var onPlayerPrivateGameStateUpdated: ((PlayerPrivateGameState) -> Void)? { get set }

    func connect(baseURL: URL) async throws
    func disconnect() async
    func attachPlayer(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot
    func setReady(roomCode: String, playerId: UUID, reconnectToken: String, isReady: Bool) async throws -> RoomSnapshot
    func playAgain(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot
    func getRoomSnapshot(roomCode: String) async throws -> RoomSnapshot
    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID) async throws -> RoomSnapshot
    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String) async throws -> RoomSnapshot
    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID) async throws -> RoomSnapshot
    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID) async throws
    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID) async throws
    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot
    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot
    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot
    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID, clientSubmissionId: UUID) async throws
    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID, clientSubmissionId: UUID) async throws
}

extension GameRealtimeClient {
    func playAgain(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot {
        throw RealtimeClientError.notConnected
    }

    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        try await submitPlayerSelection(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken, selectedPlayerId: selectedPlayerId)
    }
    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        try await submitTextAnswer(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken, text: text)
    }
    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        try await submitTextAnswerVote(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken, selectedAnswerId: selectedAnswerId)
    }
    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID, clientSubmissionId: UUID) async throws {
        try await submitPhotoAnswerVote(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken, questionInstanceId: questionInstanceId, photoAnswerId: photoAnswerId)
    }
    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID, clientSubmissionId: UUID) async throws {
        try await submitDrawingAnswerVote(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken, questionInstanceId: questionInstanceId, drawingAnswerId: drawingAnswerId)
    }
    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID) async throws {
        throw RealtimeClientError.notConnected
    }
    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID) async throws {
        throw RealtimeClientError.notConnected
    }
}

@MainActor
final class SignalRGameRealtimeClient: GameRealtimeClient {
    private struct Attachment {
        let roomCode: String
        let playerId: UUID
        let reconnectToken: String
    }

    private var connection: HubConnection?
    private var connectionBaseURL: URL?
    private var connectTask: Task<Void, Error>?
    private var attachment: Attachment?

    private(set) var status: RealtimeConnectionStatus = .disconnected {
        didSet { onStatusChanged?(status) }
    }
    var onStatusChanged: ((RealtimeConnectionStatus) -> Void)?
    var onSnapshot: ((RoomSnapshot) -> Void)?
    var onRoomStarted: ((RoomSnapshot) -> Void)?
    var onPlayerPrivateGameStateUpdated: ((PlayerPrivateGameState) -> Void)?

    func connect(baseURL: URL) async throws {
        if connectionBaseURL != nil, connectionBaseURL != baseURL {
            await disconnect()
        }
        if status == .connected { return }
        if let connectTask {
            try await connectTask.value
            return
        }

        if connection == nil {
            connection = await buildConnection(baseURL: baseURL)
            connectionBaseURL = baseURL
        }
        guard let connection else { throw RealtimeClientError.connectionUnavailable }

        status = .connecting
        let task = Task { try await connection.start() }
        connectTask = task
        defer { connectTask = nil }
        do {
            try await task.value
            status = .connected
        } catch {
            status = .failed(String(localized: "error.signalr"))
            throw RealtimeClientError.connectionFailed
        }
    }

    func disconnect() async {
        connectTask?.cancel()
        connectTask = nil
        if let connection { await connection.stop() }
        connection = nil
        connectionBaseURL = nil
        attachment = nil
        status = .disconnected
    }

    func attachPlayer(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        let result: RoomSnapshot = try await connection.invoke(
            method: "AttachPlayer",
            arguments: roomCode, playerId.uuidString, reconnectToken
        )
        attachment = Attachment(roomCode: roomCode, playerId: playerId, reconnectToken: reconnectToken)
        return result
    }

    func setReady(roomCode: String, playerId: UUID, reconnectToken: String, isReady: Bool) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(
            method: "SetReady",
            arguments: roomCode, playerId.uuidString, reconnectToken, isReady
        )
    }

    func playAgain(roomCode: String, playerId: UUID, reconnectToken: String) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(method: "PlayAgain", arguments: roomCode, playerId.uuidString, reconnectToken)
    }

    func getRoomSnapshot(roomCode: String) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(method: "GetRoomSnapshot", arguments: roomCode)
    }

    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(
            method: "SubmitPlayerSelection",
            arguments: roomCode, playerId.uuidString, reconnectToken, selectedPlayerId.uuidString
        )
    }

    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(
            method: "SubmitTextAnswer",
            arguments: roomCode, playerId.uuidString, reconnectToken, text
        )
    }

    func submitPlayerSelection(roomCode: String, playerId: UUID, reconnectToken: String, selectedPlayerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(method: "SubmitPlayerSelectionWithSubmission", arguments: roomCode, playerId.uuidString, reconnectToken, selectedPlayerId.uuidString, questionInstanceId.uuidString, clientSubmissionId.uuidString)
    }

    func submitTextAnswer(roomCode: String, playerId: UUID, reconnectToken: String, text: String, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(method: "SubmitTextAnswerWithSubmission", arguments: roomCode, playerId.uuidString, reconnectToken, text, questionInstanceId.uuidString, clientSubmissionId.uuidString)
    }

    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID, questionInstanceId: UUID, clientSubmissionId: UUID) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(method: "SubmitTextAnswerVoteWithSubmission", arguments: roomCode, playerId.uuidString, reconnectToken, selectedAnswerId.uuidString, questionInstanceId.uuidString, clientSubmissionId.uuidString)
    }

    func submitTextAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, selectedAnswerId: UUID) async throws -> RoomSnapshot {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        return try await connection.invoke(
            method: "SubmitTextAnswerVote",
            arguments: roomCode, playerId.uuidString, reconnectToken, selectedAnswerId.uuidString
        )
    }

    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID) async throws {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        try await connection.invoke(
            method: "SubmitPhotoAnswerVote",
            arguments: roomCode, playerId.uuidString, reconnectToken, questionInstanceId.uuidString, photoAnswerId.uuidString
        )
    }

    func submitPhotoAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, photoAnswerId: UUID, clientSubmissionId: UUID) async throws {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        try await connection.invoke(method: "SubmitPhotoAnswerVoteWithSubmission", arguments: roomCode, playerId.uuidString, reconnectToken, questionInstanceId.uuidString, photoAnswerId.uuidString, clientSubmissionId.uuidString)
    }

    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID) async throws {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        try await connection.invoke(
            method: "SubmitDrawingAnswerVote",
            arguments: roomCode, playerId.uuidString, reconnectToken, questionInstanceId.uuidString, drawingAnswerId.uuidString
        )
    }

    func submitDrawingAnswerVote(roomCode: String, playerId: UUID, reconnectToken: String, questionInstanceId: UUID, drawingAnswerId: UUID, clientSubmissionId: UUID) async throws {
        guard let connection, status == .connected else { throw RealtimeClientError.notConnected }
        try await connection.invoke(method: "SubmitDrawingAnswerVoteWithSubmission", arguments: roomCode, playerId.uuidString, reconnectToken, questionInstanceId.uuidString, drawingAnswerId.uuidString, clientSubmissionId.uuidString)
    }

    private func buildConnection(baseURL: URL) async -> HubConnection {
        let hubURL = baseURL.appendingPathComponent("hubs/game").absoluteString
        let result = HubConnectionBuilder()
            .withUrl(url: hubURL)
            .withHubProtocol(hubProtocol: .json)
            .withAutomaticReconnect(retryDelays: [0, 2, 5, 10, 20])
            .withLogLevel(logLevel: .warning)
            .build()

        await result.on("RoomSnapshotUpdated") { [weak self] (snapshot: RoomSnapshot) in
            await MainActor.run { self?.onSnapshot?(snapshot) }
        }
        await result.on("RoomStarted") { [weak self] (snapshot: RoomSnapshot) in
            await MainActor.run { self?.onRoomStarted?(snapshot) }
        }
        await result.on("PlayerPrivateGameStateUpdated") { [weak self] (state: PlayerPrivateGameState) in
            await MainActor.run { self?.onPlayerPrivateGameStateUpdated?(state) }
        }
        await result.onReconnecting { [weak self] _ in
            await MainActor.run { self?.status = .reconnecting }
        }
        await result.onReconnected { [weak self] in
            await self?.reattachAfterReconnect()
        }
        await result.onClosed { [weak self] error in
            await MainActor.run {
                self?.status = error == nil ? .disconnected : .failed(String(localized: "error.signalr"))
            }
        }
        return result
    }

    private func reattachAfterReconnect() async {
        status = .connected
        guard let attachment, let connection else { return }
        do {
            let snapshot: RoomSnapshot = try await connection.invoke(
                method: "AttachPlayer",
                arguments: attachment.roomCode, attachment.playerId.uuidString, attachment.reconnectToken
            )
            onSnapshot?(snapshot)
        } catch {
            status = .failed(String(localized: "error.signalr"))
        }
    }
}

enum RealtimeClientError: LocalizedError {
    case connectionUnavailable
    case connectionFailed
    case notConnected

    var errorDescription: String? { String(localized: "error.signalr") }
}
