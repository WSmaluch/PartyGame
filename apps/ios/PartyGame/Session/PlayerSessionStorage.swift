import Foundation

protocol PlayerSessionStorageProtocol: Sendable {
    func saveSession(_ session: LocalPlayerSession, reconnectToken: String) throws
    func loadSession() throws -> (session: LocalPlayerSession, reconnectToken: String)?
    func clearSession() throws
}

struct PlayerSessionStorage: PlayerSessionStorageProtocol, @unchecked Sendable {
    static let sessionKey = "player.session"
    static let tokenAccount = "active-player-reconnect-token"

    private let defaults: UserDefaults
    private let secrets: SecretStorage
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    init(defaults: UserDefaults = .standard, secrets: SecretStorage = KeychainSecretStorage()) {
        self.defaults = defaults
        self.secrets = secrets
    }

    func saveSession(_ session: LocalPlayerSession, reconnectToken: String) throws {
        try secrets.save(reconnectToken, account: Self.tokenAccount)
        do {
            defaults.set(try encoder.encode(session), forKey: Self.sessionKey)
        } catch {
            try? secrets.delete(account: Self.tokenAccount)
            throw error
        }
    }

    func loadSession() throws -> (session: LocalPlayerSession, reconnectToken: String)? {
        guard let data = defaults.data(forKey: Self.sessionKey),
              let token = try secrets.load(account: Self.tokenAccount) else {
            return nil
        }
        return (try decoder.decode(LocalPlayerSession.self, from: data), token)
    }

    func clearSession() throws {
        defaults.removeObject(forKey: Self.sessionKey)
        try secrets.delete(account: Self.tokenAccount)
    }
}
