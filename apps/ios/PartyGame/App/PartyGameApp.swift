import SwiftUI

@main
@MainActor
struct PartyGameApp: App {
    @State private var serverConfiguration: ServerConfiguration
    @State private var sessionStore: GameSessionStore

    init() {
        let configuration = ServerConfiguration()
        if let e2eBaseURL = ProcessInfo.processInfo.environment["PARTYGAME_E2E_BACKEND_URL"] {
            try? configuration.save(e2eBaseURL)
        }
        let store = GameSessionStore(configuration: configuration)
        store.configureUITestScenario(arguments: ProcessInfo.processInfo.arguments)
        _serverConfiguration = State(initialValue: configuration)
        _sessionStore = State(initialValue: store)
    }

    var body: some Scene {
        WindowGroup {
            HomeView(configuration: serverConfiguration, store: sessionStore)
        }
    }
}
