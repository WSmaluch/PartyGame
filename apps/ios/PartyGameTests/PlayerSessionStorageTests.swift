import XCTest
@testable import PartyGame

final class PlayerSessionStorageTests: XCTestCase {
    func testStoresMetadataInDefaultsAndTokenInSecretStorage() throws {
        let suite = "PartyGameSessionTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let secrets = MemorySecretStorage()
        let storage = PlayerSessionStorage(defaults: defaults, secrets: secrets)
        let session = LocalPlayerSession(roomCode: "ABCD", playerId: UUID(), nickname: "Ola", isHost: true, serverBaseURL: "http://localhost:5050")

        try storage.saveSession(session, reconnectToken: "very-secret-token")

        XCTAssertNotNil(defaults.data(forKey: PlayerSessionStorage.sessionKey))
        XCTAssertFalse(String(data: defaults.data(forKey: PlayerSessionStorage.sessionKey)!, encoding: .utf8)!.contains("very-secret-token"))
        XCTAssertEqual(try secrets.load(account: PlayerSessionStorage.tokenAccount), "very-secret-token")
        let loaded = try storage.loadSession()
        XCTAssertEqual(loaded?.session, session)
        XCTAssertEqual(loaded?.reconnectToken, "very-secret-token")

        try storage.clearSession()
        XCTAssertNil(try storage.loadSession())
    }
}

private final class MemorySecretStorage: SecretStorage, @unchecked Sendable {
    private var values: [String: String] = [:]
    func save(_ value: String, account: String) throws { values[account] = value }
    func load(account: String) throws -> String? { values[account] }
    func delete(account: String) throws { values.removeValue(forKey: account) }
}
