import SwiftUI

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
