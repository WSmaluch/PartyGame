import XCTest
@testable import PartyGame

final class ServerConfigurationTests: XCTestCase {
    private var defaults: UserDefaults!
    private var suiteName: String!

    override func setUp() {
        super.setUp()
        suiteName = "PartyGameTests.\(UUID().uuidString)"
        defaults = UserDefaults(suiteName: suiteName)!
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suiteName)
        defaults = nil
        suiteName = nil
        super.tearDown()
    }

    func testAddressValidationAcceptsHTTPAndHTTPS() {
        XCTAssertNotNil(ServerConfiguration.validatedURL(from: "http://192.168.1.100:5050"))
        XCTAssertNotNil(ServerConfiguration.validatedURL(from: "https://example.com"))
    }

    func testAddressValidationRejectsIncompleteAndUnsafeValues() {
        XCTAssertNil(ServerConfiguration.validatedURL(from: "192.168.1.100:5050"))
        XCTAssertNil(ServerConfiguration.validatedURL(from: "ftp://example.com"))
        XCTAssertNil(ServerConfiguration.validatedURL(from: "http://example.com/path"))
    }

    func testBuildsHealthEndpointURL() throws {
        let configuration = ServerConfiguration(defaults: defaults)
        try configuration.save("http://192.168.1.10:5050/")

        XCTAssertEqual(try configuration.healthURL().absoluteString, "http://192.168.1.10:5050/health")
    }

    func testPersistsAndReadsServerAddress() throws {
        let configuration = ServerConfiguration(defaults: defaults)
        try configuration.save("http://10.0.0.5:5050")

        let restored = ServerConfiguration(defaults: defaults)
        XCTAssertEqual(restored.baseURL, "http://10.0.0.5:5050")
    }
}
