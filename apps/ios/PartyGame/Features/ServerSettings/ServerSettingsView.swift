import SwiftUI
import UIKit

struct ServerSettingsView: View {
    let configuration: ServerConfiguration
    let healthViewModel: ServerHealthViewModel

    @Environment(\.dismiss) private var dismiss
    @State private var draftAddress: String
    @State private var validationMessage: String?

    init(configuration: ServerConfiguration, healthViewModel: ServerHealthViewModel) {
        self.configuration = configuration
        self.healthViewModel = healthViewModel
        _draftAddress = State(initialValue: configuration.baseURL)
    }

    var body: some View {
        Form {
            Section("server.settings.address_section") {
                TextField("server.settings.placeholder", text: $draftAddress)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .keyboardType(.URL)

                if let validationMessage {
                    Text(validationMessage).foregroundStyle(.red).font(.footnote)
                }

                Button("server.check_connection") { saveAndCheck() }
                Button("server.restore_default", role: .destructive) {
                    configuration.restoreDefault()
                    draftAddress = configuration.baseURL
                    validationMessage = nil
                    healthViewModel.checkConnection()
                }
            }

            Section {
                Text("server.settings.local_http_note")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }

            Section("Diagnostyka") {
                Text("Wersja aplikacji: \(appVersion)")
                Text("Serwer: \(configuration.baseURL)")
                switch healthViewModel.state {
                case let .online(response):
                    Text("Wersja serwera: \(response.version)")
                    Text("Stan połączenia: online")
                case .loading:
                    Text("Stan połączenia: sprawdzanie")
                case .offline:
                    Text("Stan połączenia: offline")
                case .idle:
                    Text("Stan połączenia: nie sprawdzono")
                }
                Text("Reconnect token nie jest wyświetlany ani kopiowany.")
                    .font(.footnote).foregroundStyle(.secondary)
                Button("Kopiuj bezpieczne podsumowanie") {
                    UIPasteboard.general.string = safeSummary
                }
            }
        }
        .navigationTitle("server.settings.title")
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("common.done") {
                    guard saveAddress() else { return }
                    dismiss()
                }
            }
        }
    }

    private func saveAndCheck() {
        guard saveAddress() else { return }
        healthViewModel.checkConnection()
    }

    private var appVersion: String {
        let version = Bundle.main.object(forInfoDictionaryKey: "PartyGameReleaseVersion") as? String
            ?? Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
            ?? "unknown"
        let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "unknown"
        return "\(version) (\(build))"
    }

    private var safeSummary: String {
        let serverVersion: String
        let state: String
        switch healthViewModel.state {
        case let .online(response): serverVersion = response.version; state = "online"
        case .loading: serverVersion = "—"; state = "checking"
        case .offline: serverVersion = "—"; state = "offline"
        case .idle: serverVersion = "—"; state = "idle"
        }
        return "PartyGame iOS\nApp: \(appVersion)\nServer: \(configuration.baseURL)\nServer version: \(serverVersion)\nConnection: \(state)"
    }

    @discardableResult
    private func saveAddress() -> Bool {
        do {
            try configuration.save(draftAddress)
            draftAddress = configuration.baseURL
            validationMessage = nil
            return true
        } catch {
            validationMessage = error.localizedDescription
            return false
        }
    }
}
