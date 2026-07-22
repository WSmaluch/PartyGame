import SwiftUI

struct ServerStatusCard: View {
    let state: ServerHealthViewModel.State
    let baseURL: String
    let retry: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Label(statusTitle, systemImage: statusIcon)
                    .font(.headline)
                    .foregroundStyle(statusColor)
                Spacer()
                if case .loading = state {
                    ProgressView()
                }
            }

            switch state {
            case let .online(response):
                detailRow("server.service", response.service)
                detailRow("server.version", response.version)
                detailRow("server.utc_time", response.utcTime)
            case let .offline(message):
                Text(message).font(.footnote).foregroundStyle(.red)
            case .idle, .loading:
                Text("server.checking").font(.footnote).foregroundStyle(.secondary)
            }

            detailRow("server.address", baseURL)

            Button("common.retry", action: retry)
                .buttonStyle(.bordered)
                .disabled(state == .loading)
        }
        .padding(20)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 24, style: .continuous))
    }

    private var statusTitle: LocalizedStringKey {
        switch state {
        case .online: "server.status.online"
        case .loading: "server.status.checking"
        case .idle, .offline: "server.status.offline"
        }
    }

    private var statusIcon: String {
        switch state {
        case .online: "checkmark.circle.fill"
        case .loading: "arrow.triangle.2.circlepath"
        case .idle, .offline: "xmark.circle.fill"
        }
    }

    private var statusColor: Color {
        switch state {
        case .online: .green
        case .loading: .orange
        case .idle, .offline: .red
        }
    }

    private func detailRow(_ key: LocalizedStringKey, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(key).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.footnote.monospaced()).textSelection(.enabled)
        }
    }
}
