import SwiftUI

struct ReconnectingView: View {
    let store: GameSessionStore
    var body: some View {
        VStack(spacing: 20) {
            ProgressView().controlSize(.large)
            Text("reconnect.title").font(.title.bold())
            Text("reconnect.message").multilineTextAlignment(.center).foregroundStyle(.secondary)
            if let error = store.errorMessage { Text(error).foregroundStyle(.red).multilineTextAlignment(.center) }
            Button("common.retry") { Task { await store.retryConnection() } }.buttonStyle(.borderedProminent)
            Button("session.forget", role: .destructive) { Task { await store.forgetSession() } }
        }
        .padding(28).navigationBarBackButtonHidden()
    }
}
