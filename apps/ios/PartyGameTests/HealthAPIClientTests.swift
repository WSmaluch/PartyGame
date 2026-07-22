import Foundation
import XCTest
@testable import PartyGame

final class HealthAPIClientTests: XCTestCase {
    private var client: HealthAPIClient!

    override func setUp() {
        super.setUp()
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [URLProtocolStub.self]
        client = HealthAPIClient(session: URLSession(configuration: configuration))
    }

    override func tearDown() {
        URLProtocolStub.handler = nil
        client = nil
        super.tearDown()
    }

    func testDecodesValidHealthResponse() async throws {
        URLProtocolStub.handler = { request in
            let data = #"{"status":"ok","service":"PartyGame.Api","version":"1.0.0.0","utcTime":"2026-07-20T12:00:00Z"}"#.data(using: .utf8)!
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, data)
        }

        let response = try await client.fetchHealth(from: URL(string: "http://localhost:5050/health")!)

        XCTAssertEqual(response.status, "ok")
        XCTAssertEqual(response.service, "PartyGame.Api")
        XCTAssertEqual(response.version, "1.0.0.0")
    }

    func testReportsInvalidJSON() async {
        URLProtocolStub.handler = { request in
            let data = Data("not-json".utf8)
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, data)
        }

        do {
            _ = try await client.fetchHealth(from: URL(string: "http://localhost:5050/health")!)
            XCTFail("Expected invalid JSON error")
        } catch HealthAPIError.invalidJSON {
            // Expected.
        } catch {
            XCTFail("Unexpected error: \(error)")
        }
    }

    func testReportsHTTPError() async {
        URLProtocolStub.handler = { request in
            (HTTPURLResponse(url: request.url!, statusCode: 503, httpVersion: nil, headerFields: nil)!, Data())
        }

        do {
            _ = try await client.fetchHealth(from: URL(string: "http://localhost:5050/health")!)
            XCTFail("Expected HTTP error")
        } catch HealthAPIError.httpStatus(503) {
            // Expected.
        } catch {
            XCTFail("Unexpected error: \(error)")
        }
    }
}

private final class URLProtocolStub: URLProtocol {
    static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        guard let handler = Self.handler else {
            client?.urlProtocol(self, didFailWithError: URLError(.unknown))
            return
        }
        do {
            let (response, data) = try handler(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}
