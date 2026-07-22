import Foundation

protocol RoomAPIClientProtocol: Sendable {
    func createRoom(baseURL: URL, request: CreateRoomRequest) async throws -> CreateRoomResponse
    func joinRoom(baseURL: URL, roomCode: String, request: JoinRoomRequest) async throws -> JoinRoomResponse
    func getRoom(baseURL: URL, roomCode: String) async throws -> RoomSnapshot
    func resume(baseURL: URL, session: LocalPlayerSession, reconnectToken: String) async throws -> ResumePlayerResponse
    func uploadProfilePhoto(baseURL: URL, session: LocalPlayerSession, reconnectToken: String, jpegData: Data) async throws -> RoomSnapshot
    func profilePhotoURL(baseURL: URL, relativePath: String) -> URL?
    func getContentPackages(baseURL: URL) async throws -> [ContentPackage]
    func uploadPhotoAnswer(
        baseURL: URL, session: LocalPlayerSession, reconnectToken: String, questionInstanceId: UUID,
        clientSubmissionId: UUID, jpegData: Data, progress: @escaping @Sendable (Double) -> Void
    ) async throws -> PhotoAnswerUploadResponse
    func uploadDrawingAnswer(
        baseURL: URL, session: LocalPlayerSession, reconnectToken: String, questionInstanceId: UUID,
        clientSubmissionId: UUID, pngData: Data, progress: @escaping @Sendable (Double) -> Void
    ) async throws -> DrawingAnswerUploadResponse
}

extension RoomAPIClientProtocol {
    func uploadPhotoAnswer(
        baseURL: URL, session: LocalPlayerSession, reconnectToken: String, questionInstanceId: UUID,
        clientSubmissionId: UUID, jpegData: Data, progress: @escaping @Sendable (Double) -> Void
    ) async throws -> PhotoAnswerUploadResponse { throw RoomAPIError.invalidRequest }
    func uploadDrawingAnswer(
        baseURL: URL, session: LocalPlayerSession, reconnectToken: String, questionInstanceId: UUID,
        clientSubmissionId: UUID, pngData: Data, progress: @escaping @Sendable (Double) -> Void
    ) async throws -> DrawingAnswerUploadResponse { throw RoomAPIError.invalidRequest }
}

struct PhotoAnswerUploadResponse: Codable, Equatable, Sendable {
    let photoAnswerId: UUID
    let playerPrivateGameState: PlayerPrivateGameState
    let roomSnapshot: RoomSnapshot
}

struct DrawingAnswerUploadResponse: Codable, Equatable, Sendable {
    let drawingAnswerId: UUID
    let playerPrivateGameState: PlayerPrivateGameState
    let roomSnapshot: RoomSnapshot
}

struct RoomAPIClient: RoomAPIClientProtocol, Sendable {
    private let session: URLSession
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    init(session: URLSession = .shared) {
        self.session = session
        encoder = JSONEncoder()
        decoder = JSONDecoder()
    }

    func createRoom(baseURL: URL, request: CreateRoomRequest) async throws -> CreateRoomResponse {
        try await sendJSON(baseURL: baseURL, path: "/api/rooms", method: "POST", body: request)
    }

    func joinRoom(baseURL: URL, roomCode: String, request: JoinRoomRequest) async throws -> JoinRoomResponse {
        try await sendJSON(baseURL: baseURL, path: "/api/rooms/\(roomCode)/players", method: "POST", body: request)
    }

    func getRoom(baseURL: URL, roomCode: String) async throws -> RoomSnapshot {
        try await send(baseURL: baseURL, path: "/api/rooms/\(roomCode)", method: "GET")
    }

    func resume(baseURL: URL, session: LocalPlayerSession, reconnectToken: String) async throws -> ResumePlayerResponse {
        try await send(
            baseURL: baseURL,
            path: "/api/rooms/\(session.roomCode)/players/\(session.playerId.uuidString)/resume",
            method: "POST",
            token: reconnectToken
        )
    }

    func uploadProfilePhoto(baseURL: URL, session: LocalPlayerSession, reconnectToken: String, jpegData: Data) async throws -> RoomSnapshot {
        let multipart = MultipartFormDataBuilder.profilePhoto(jpegData: jpegData)
        return try await send(
            baseURL: baseURL,
            path: "/api/rooms/\(session.roomCode)/players/\(session.playerId.uuidString)/profile-photo",
            method: "POST",
            token: reconnectToken,
            contentType: multipart.contentType,
            body: multipart.body
        )
    }

    func profilePhotoURL(baseURL: URL, relativePath: String) -> URL? {
        guard let components = URLComponents(string: relativePath), components.scheme == nil, components.host == nil else {
            return URL(string: relativePath)
        }
        return URL(string: relativePath, relativeTo: baseURL)?.absoluteURL
    }

    func getContentPackages(baseURL: URL) async throws -> [ContentPackage] {
        try await send(baseURL: baseURL, path: "/api/content/packages", method: "GET")
    }

    func uploadPhotoAnswer(
        baseURL: URL,
        session playerSession: LocalPlayerSession,
        reconnectToken: String,
        questionInstanceId: UUID,
        clientSubmissionId: UUID,
        jpegData: Data,
        progress: @escaping @Sendable (Double) -> Void
    ) async throws -> PhotoAnswerUploadResponse {
        let multipart = MultipartFormDataBuilder.photoAnswer(
            playerId: playerSession.playerId,
            reconnectToken: reconnectToken,
            clientSubmissionId: clientSubmissionId,
            jpegData: jpegData
        )
        guard let url = URL(string: "/api/rooms/\(playerSession.roomCode)/questions/\(questionInstanceId.uuidString)/photo-answers", relativeTo: baseURL)?.absoluteURL else {
            throw RoomAPIError.invalidRequest
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 45
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(multipart.contentType, forHTTPHeaderField: "Content-Type")
        let delegate = UploadProgressDelegate(progress: progress)
        do {
            let (data, response) = try await self.session.upload(for: request, from: multipart.body, delegate: delegate)
            guard let http = response as? HTTPURLResponse else { throw RoomAPIError.invalidResponse }
            guard (200 ... 299).contains(http.statusCode) else {
                throw RoomAPIError.http(status: http.statusCode, problem: try? decoder.decode(ProblemDetails.self, from: data))
            }
            guard let decoded = try? decoder.decode(PhotoAnswerUploadResponse.self, from: data) else { throw RoomAPIError.invalidData }
            return decoded
        } catch is CancellationError { throw RoomAPIError.cancelled }
        catch let error as URLError where error.code == .timedOut { throw RoomAPIError.timeout }
        catch let error as URLError where error.code == .cancelled { throw RoomAPIError.cancelled }
        catch let error as RoomAPIError { throw error }
        catch { throw RoomAPIError.networkUnavailable }
    }

    func uploadDrawingAnswer(
        baseURL: URL,
        session playerSession: LocalPlayerSession,
        reconnectToken: String,
        questionInstanceId: UUID,
        clientSubmissionId: UUID,
        pngData: Data,
        progress: @escaping @Sendable (Double) -> Void
    ) async throws -> DrawingAnswerUploadResponse {
        let multipart = MultipartFormDataBuilder.drawingAnswer(
            playerId: playerSession.playerId, reconnectToken: reconnectToken,
            clientSubmissionId: clientSubmissionId, pngData: pngData
        )
        guard let url = URL(string: "/api/rooms/\(playerSession.roomCode)/questions/\(questionInstanceId.uuidString)/drawing-answers", relativeTo: baseURL)?.absoluteURL else {
            throw RoomAPIError.invalidRequest
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 45
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(multipart.contentType, forHTTPHeaderField: "Content-Type")
        let delegate = UploadProgressDelegate(progress: progress)
        do {
            let (data, response) = try await session.upload(for: request, from: multipart.body, delegate: delegate)
            guard let http = response as? HTTPURLResponse else { throw RoomAPIError.invalidResponse }
            guard (200 ... 299).contains(http.statusCode) else {
                throw RoomAPIError.http(status: http.statusCode, problem: try? decoder.decode(ProblemDetails.self, from: data))
            }
            guard let decoded = try? decoder.decode(DrawingAnswerUploadResponse.self, from: data) else { throw RoomAPIError.invalidData }
            return decoded
        } catch is CancellationError { throw RoomAPIError.cancelled }
        catch let error as URLError where error.code == .timedOut { throw RoomAPIError.timeout }
        catch let error as URLError where error.code == .cancelled { throw RoomAPIError.cancelled }
        catch let error as RoomAPIError { throw error }
        catch { throw RoomAPIError.networkUnavailable }
    }

    private func sendJSON<TBody: Encodable, TResponse: Decodable>(
        baseURL: URL,
        path: String,
        method: String,
        body: TBody
    ) async throws -> TResponse {
        let data: Data
        do {
            data = try encoder.encode(body)
        } catch {
            throw RoomAPIError.invalidRequest
        }
        return try await send(baseURL: baseURL, path: path, method: method, contentType: "application/json", body: data)
    }

    private func send<TResponse: Decodable>(
        baseURL: URL,
        path: String,
        method: String,
        token: String? = nil,
        contentType: String? = nil,
        body: Data? = nil
    ) async throws -> TResponse {
        guard let url = URL(string: path, relativeTo: baseURL)?.absoluteURL else {
            throw RoomAPIError.invalidRequest
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.httpBody = body
        request.timeoutInterval = 15
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token { request.setValue(token, forHTTPHeaderField: "X-Player-Token") }
        if let contentType { request.setValue(contentType, forHTTPHeaderField: "Content-Type") }

        do {
            let (data, response) = try await session.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse else {
                throw RoomAPIError.invalidResponse
            }
            guard (200 ... 299).contains(httpResponse.statusCode) else {
                let problem = try? decoder.decode(ProblemDetails.self, from: data)
                throw RoomAPIError.http(status: httpResponse.statusCode, problem: problem)
            }
            do {
                return try decoder.decode(TResponse.self, from: data)
            } catch {
                throw RoomAPIError.invalidData
            }
        } catch is CancellationError {
            throw RoomAPIError.cancelled
        } catch let error as URLError {
            switch error.code {
            case .cancelled: throw RoomAPIError.cancelled
            case .timedOut: throw RoomAPIError.timeout
            case .notConnectedToInternet, .networkConnectionLost, .cannotConnectToHost, .cannotFindHost, .dnsLookupFailed:
                throw RoomAPIError.networkUnavailable
            default: throw RoomAPIError.transport
            }
        } catch let error as RoomAPIError {
            throw error
        }
    }
}

private final class UploadProgressDelegate: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    private let progress: @Sendable (Double) -> Void
    init(progress: @escaping @Sendable (Double) -> Void) { self.progress = progress }

    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        guard totalBytesExpectedToSend > 0 else { return }
        progress(min(1, Double(totalBytesSent) / Double(totalBytesExpectedToSend)))
    }
}

enum RoomAPIError: LocalizedError, Equatable {
    case invalidRequest
    case invalidResponse
    case invalidData
    case cancelled
    case timeout
    case networkUnavailable
    case transport
    case http(status: Int, problem: ProblemDetails?)

    var isInvalidSession: Bool {
        if case let .http(status, _) = self { return status == 401 || status == 404 }
        return false
    }

    var errorDescription: String? {
        switch self {
        case .invalidRequest: String(localized: "error.invalid_request")
        case .invalidResponse: String(localized: "error.invalid_response")
        case .invalidData: String(localized: "error.invalid_json")
        case .cancelled: String(localized: "error.request_cancelled")
        case .timeout: String(localized: "error.timeout")
        case .networkUnavailable, .transport: String(localized: "error.network_unavailable")
        case let .http(status, problem): problem?.userMessage ?? String(format: String(localized: "error.http_status"), status)
        }
    }
}
