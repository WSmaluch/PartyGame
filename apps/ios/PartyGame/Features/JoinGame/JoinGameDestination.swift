import SwiftUI

struct JoinGameDestination: View {
    let store: GameSessionStore
    @State private var roomCode = ""
    @State private var nickname = ""

    var body: some View {
        Form {
            Section("join.details") {
                TextField("join.room_code", text: $roomCode)
                    .textInputAutocapitalization(.characters)
                    .autocorrectionDisabled()
                    .font(.title2.monospaced().bold())
                    .onChange(of: roomCode) { _, value in roomCode = GameSessionStore.normalizedRoomCode(value) }
                    .accessibilityIdentifier("join.roomCode")
                TextField("player.nickname", text: $nickname)
                    .textInputAutocapitalization(.words)
                    .accessibilityIdentifier("join.nickname")
            }
            if let error = store.errorMessage { Text(error).foregroundStyle(.red) }
            Section {
                Button("join.action") { Task { await store.joinRoom(roomCode: roomCode, nickname: nickname) } }
                    .disabled(!valid || store.isWorking)
                    .accessibilityIdentifier("join.submit")
                if store.isWorking { ProgressView() }
            }
        }
        .navigationTitle("join.title")
        .toolbar { ToolbarItem(placement: .topBarLeading) { Button("common.back") { store.showHome() }.accessibilityIdentifier("common.back") } }
    }

    private var valid: Bool {
        roomCode.count == 4 && (2 ... 20).contains(nickname.trimmingCharacters(in: .whitespaces).count)
    }
}
