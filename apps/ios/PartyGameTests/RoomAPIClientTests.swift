import Foundation
import XCTest
@testable import PartyGame

final class RoomAPIClientTests: XCTestCase {
    private var client: RoomAPIClient!

    override func setUp() {
        super.setUp()
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [RoomURLProtocolStub.self]
        client = RoomAPIClient(session: URLSession(configuration: configuration))
    }

    override func tearDown() {
        RoomURLProtocolStub.handler = nil
        client = nil
        super.tearDown()
    }

    func testCreateRoomUsesExpectedEndpointAndDecodesResponse() async throws {
        RoomURLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.absoluteString, "http://localhost:5050/api/rooms")
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")
            let body = try Self.extractBody(from: request)
            XCTAssertEqual(try JSONDecoder().decode(CreateRoomRequest.self, from: body).nickname, "Ola")
            return Self.response(request: request, status: 201, json: Self.accessResponseJSON)
        }
        let response = try await client.createRoom(baseURL: URL(string: "http://localhost:5050")!, request: CreateRoomRequest(nickname: "Ola", settings: RoomSettings(), selectedPackageKeys: nil))
        XCTAssertEqual(response.roomCode, "ABCD")
        XCTAssertFalse(response.reconnectToken.isEmpty)
    }

    func testResumeSendsPlayerTokenHeader() async throws {
        let playerId = UUID(uuidString: "0dc81d35-c68d-47c6-aebb-5e86407a1bb0")!
        RoomURLProtocolStub.handler = { request in
            XCTAssertEqual(request.value(forHTTPHeaderField: "X-Player-Token"), "secret")
            XCTAssertTrue(request.url!.path.hasSuffix("/players/\(playerId.uuidString)/resume"))
            let json = #"{"player":{"id":"0dc81d35-c68d-47c6-aebb-5e86407a1bb0","nickname":"Ola","isHost":true,"isReady":false,"isConnected":true,"hasProfilePhoto":false,"profilePhotoUrl":null,"score":0},"snapshot":\#(Self.snapshotJSON),"privateState":\#(Self.privateStateJSON)}"#
            return Self.response(request: request, status: 200, json: json)
        }
        let session = LocalPlayerSession(roomCode: "ABCD", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: "http://localhost:5050")
        _ = try await client.resume(baseURL: URL(string: "http://localhost:5050")!, session: session, reconnectToken: "secret")
    }

    func testUploadUsesJPEGMultipartAndSafeFilename() async throws {
        let playerId = UUID(uuidString: "0dc81d35-c68d-47c6-aebb-5e86407a1bb0")!
        RoomURLProtocolStub.handler = { request in
            XCTAssertEqual(request.value(forHTTPHeaderField: "X-Player-Token"), "secret")
            XCTAssertTrue(request.value(forHTTPHeaderField: "Content-Type")!.hasPrefix("multipart/form-data; boundary="))
            let body = String(data: try Self.extractBody(from: request), encoding: .utf8)!
            XCTAssertTrue(body.contains("name=\"file\"; filename=\"profile.jpg\""))
            XCTAssertTrue(body.contains("Content-Type: image/jpeg"))
            return Self.response(request: request, status: 200, json: Self.snapshotJSON)
        }
        let session = LocalPlayerSession(roomCode: "ABCD", playerId: playerId, nickname: "Ola", isHost: true, serverBaseURL: "http://localhost:5050")
        _ = try await client.uploadProfilePhoto(baseURL: URL(string: "http://localhost:5050")!, session: session, reconnectToken: "secret", jpegData: Data([1, 2, 3]))
    }

    func testMapsProblemDetailsAndInvalidSessionStatus() async {
        RoomURLProtocolStub.handler = { request in
            Self.response(request: request, status: 401, json: #"{"title":"Unauthorized","status":401,"detail":"Token wygasł"}"#)
        }
        do {
            _ = try await client.getRoom(baseURL: URL(string: "http://localhost:5050")!, roomCode: "ABCD")
            XCTFail("Expected error")
        } catch let error as RoomAPIError {
            XCTAssertTrue(error.isInvalidSession)
            XCTAssertEqual(error.localizedDescription, "Token wygasł")
        } catch { XCTFail("Unexpected error: \(error)") }
    }

    private static func response(request: URLRequest, status: Int, json: String) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: ["Content-Type": "application/json"])!, Data(json.utf8))
    }

    private static let snapshotJSON = #"{"roomCode":"ABCD","phase":"Lobby","stateVersion":1,"displayConnected":false,"minimumPlayers":3,"maximumPlayers":8,"canStart":false,"settings":{"roundCount":4,"questionsPerRound":5,"playerSelectionSeconds":20,"textAnswerSeconds":40,"votingSeconds":20,"photoSeconds":45,"drawingSeconds":90,"resultPresentationSeconds":8,"finalRoundEnabled":true,"finalDrawingPasses":3},"players":[{"id":"0dc81d35-c68d-47c6-aebb-5e86407a1bb0","nickname":"Ola","isHost":true,"isReady":false,"isConnected":true,"hasProfilePhoto":false,"profilePhotoUrl":null,"score":0}],"createdAtUtc":"2026-07-20T12:00:00Z","startedAtUtc":null}"#
    private static let privateStateJSON = #"{"playerId":"0dc81d35-c68d-47c6-aebb-5e86407a1bb0","questionInstanceId":null,"hasSubmittedTextAnswer":false,"ownTextAnswerId":null,"hasSubmittedTextAnswerVote":false}"#
    private static let accessResponseJSON = #"{"roomCode":"ABCD","playerId":"0dc81d35-c68d-47c6-aebb-5e86407a1bb0","reconnectToken":"token","snapshot":\#(snapshotJSON),"privateState":\#(privateStateJSON)}"#

    static func extractBody(from request: URLRequest) throws -> Data {
        if let body = request.httpBody { return body }
        guard let stream = request.httpBodyStream else { throw URLError(.unknown) }
        stream.open()
        defer { stream.close() }
        var data = Data()
        let bufferSize = 1024
        var buffer = [UInt8](repeating: 0, count: bufferSize)
        while stream.hasBytesAvailable {
            let read = stream.read(&buffer, maxLength: bufferSize)
            if read > 0 { data.append(buffer, count: read) }
            else if read < 0 { throw stream.streamError ?? URLError(.unknown) }
            else { break }
        }
        return data
    }
}

private final class RoomURLProtocolStub: URLProtocol {
    static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            let (response, data) = try XCTUnwrap(Self.handler)(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() {}
}
