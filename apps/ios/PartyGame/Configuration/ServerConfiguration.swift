import Foundation
import Observation

@Observable
final class ServerConfiguration {
    static let defaultBaseURL = "http://localhost:5050"

    private static let storageKey = "server.baseURL"
    private let defaults: UserDefaults

    var baseURL: String

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        baseURL = defaults.string(forKey: Self.storageKey) ?? Self.defaultBaseURL
    }

    @discardableResult
    func save(_ value: String) throws -> URL {
        let normalized = value.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = Self.validatedURL(from: normalized) else {
            throw ServerConfigurationError.invalidAddress
        }

        baseURL = url.absoluteString.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        defaults.set(baseURL, forKey: Self.storageKey)
        return url
    }

    func restoreDefault() {
        baseURL = Self.defaultBaseURL
        defaults.set(baseURL, forKey: Self.storageKey)
    }

    func healthURL() throws -> URL {
        guard let base = Self.validatedURL(from: baseURL) else {
            throw ServerConfigurationError.invalidAddress
        }
        return base.appendingPathComponent("health")
    }

    static func validatedURL(from value: String) -> URL? {
        guard let components = URLComponents(string: value),
              let scheme = components.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              components.host?.isEmpty == false,
              components.user == nil,
              components.password == nil,
              components.query == nil,
              components.fragment == nil,
              components.path.isEmpty || components.path == "/",
              let url = components.url else {
            return nil
        }
        return url
    }
}

enum ServerConfigurationError: LocalizedError {
    case invalidAddress

    var errorDescription: String? {
        String(localized: "error.invalid_server_address")
    }
}
