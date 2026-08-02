import SwiftUI

struct LobbyView: View {
    let store: GameSessionStore
    @State private var confirmsForget = false

    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                Text("lobby.room_code").font(.headline).foregroundStyle(.secondary)
                Text(store.snapshot?.roomCode ?? "—")
                    .font(.system(size: 52, weight: .black, design: .monospaced))
                    .textSelection(.enabled)
                    .accessibilityIdentifier("lobby.roomCode")

                Label(store.snapshot?.displayConnected == true ? "lobby.display_connected" : "lobby.display_waiting",
                      systemImage: store.snapshot?.displayConnected == true ? "tv.fill" : "tv")
                    .foregroundStyle(store.snapshot?.displayConnected == true ? .green : .orange)

                LazyVGrid(columns: [GridItem(.adaptive(minimum: 140))], spacing: 14) {
                    ForEach(store.snapshot?.players ?? []) { player in playerCard(player) }
                }

                if let player = store.ownPlayer {
                    Button(player.isReady ? "lobby.not_ready" : "lobby.ready") {
                        Task { await store.setReady(!player.isReady) }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(store.isWorking)
                    .accessibilityIdentifier("lobby.ready")
                }
                Button("lobby.change_photo") { store.showPhotoCapture() }.buttonStyle(.bordered)
                Button("session.forget", role: .destructive) { confirmsForget = true }
            }
            .padding(24)
        }
        .navigationTitle("lobby.title")
        .navigationBarBackButtonHidden()
        .overlay(alignment: .topLeading) {
            if let snapshot = store.snapshot {
                ZStack {
                    Color.clear.frame(width: 1, height: 1)
                        .accessibilityElement()
                        .accessibilityIdentifier(SnapshotAccessibilityMetadata.identifier(snapshot: snapshot, phase: snapshot.phase.rawValue, questionId: nil))
                    Color.clear.frame(width: 1, height: 1)
                        .accessibilityElement()
                        .accessibilityIdentifier("game.connection|state=\(store.realtimeDiagnosticState)")
                }
            }
        }
        .alert("session.forget.title", isPresented: $confirmsForget) {
            Button("common.cancel", role: .cancel) {}
            Button("session.forget", role: .destructive) { Task { await store.forgetSession() } }
        } message: { Text("session.forget.message") }
    }

    private func playerCard(_ player: RoomPlayer) -> some View {
        VStack(spacing: 8) {
            AsyncImage(url: cacheBustedURL(for: player)) { image in
                image.resizable().scaledToFill()
            } placeholder: {
                Image(systemName: "person.crop.circle.fill").resizable().foregroundStyle(.secondary)
            }
            .frame(width: 82, height: 82).clipShape(Circle())
            Text(player.nickname).font(.headline).lineLimit(1)
            HStack(spacing: 5) {
                if player.isHost { Image(systemName: "crown.fill").foregroundStyle(.yellow) }
                Image(systemName: player.isConnected ? "wifi" : "wifi.slash")
                Image(systemName: player.isReady ? "checkmark.circle.fill" : "circle")
                    .foregroundStyle(player.isReady ? .green : .secondary)
            }
        }
        .padding().frame(maxWidth: .infinity)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 18))
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(player.nickname), \(player.isReady ? String(localized: "lobby.ready_status") : String(localized: "lobby.not_ready_status"))")
    }

    private func cacheBustedURL(for player: RoomPlayer) -> URL? {
        guard let url = store.profilePhotoURL(for: player) else { return nil }
        return url.appending(queryItems: [URLQueryItem(name: "v", value: String(store.snapshot?.stateVersion ?? 0))])
    }
}
