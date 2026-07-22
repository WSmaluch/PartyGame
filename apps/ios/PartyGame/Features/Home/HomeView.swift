import SwiftUI

struct HomeView: View {
    let configuration: ServerConfiguration
    let store: GameSessionStore
    @Environment(\.scenePhase) private var scenePhase
    @State private var healthViewModel: ServerHealthViewModel

    init(configuration: ServerConfiguration, store: GameSessionStore) {
        self.configuration = configuration
        self.store = store
        _healthViewModel = State(initialValue: ServerHealthViewModel(configuration: configuration))
    }

    var body: some View {
        NavigationStack {
            Group {
                switch store.screen {
                case .idle: home
                case .hostSetup: HostGameDestination(store: store)
                case .joinSetup: JoinGameDestination(store: store)
                case .profilePhoto: ProfilePhotoView(store: store)
                case .lobby: LobbyView(store: store)
                case .reconnecting: ReconnectingView(store: store)
                case .started: GameRouterView(store: store)
                }
            }
            .background(background.ignoresSafeArea())
        }
        .task { await store.restoreSession() }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await store.applicationBecameActive() } }
        }
        .onChange(of: configuration.baseURL) { _, _ in
            Task { await store.serverAddressChanged() }
        }
    }

    private var home: some View {
        ScrollView {
            VStack(spacing: 24) {
                Text("app.title")
                    .font(.system(size: 54, weight: .black, design: .rounded))
                    .foregroundStyle(LinearGradient(colors: [.pink, .purple, .blue], startPoint: .leading, endPoint: .trailing))
                    .padding(.top, 32)
                Text("home.subtitle").font(.headline).foregroundStyle(.secondary)

                Button { store.showHostSetup() } label: {
                    PartyButton(titleKey: "home.host_game", colors: [.pink, .purple])
                }
                .accessibilityIdentifier("home.hostGame")

                Button { store.showJoinSetup() } label: {
                    PartyButton(titleKey: "home.join_game", colors: [.blue, .cyan])
                }
                .accessibilityIdentifier("home.joinGame")

                ServerStatusCard(state: healthViewModel.state, baseURL: configuration.baseURL, retry: healthViewModel.checkConnection)
            }
            .padding(24)
        }
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                NavigationLink {
                    ServerSettingsView(configuration: configuration, healthViewModel: healthViewModel)
                } label: {
                    Label("server.settings.title", systemImage: "gearshape.fill")
                }
            }
        }
        .task { healthViewModel.checkConnection() }
        .onDisappear { healthViewModel.cancel() }
    }

    private var background: some View {
        LinearGradient(
            colors: [Color.indigo.opacity(0.16), Color.pink.opacity(0.12), Color.cyan.opacity(0.08)],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
        )
    }
}
