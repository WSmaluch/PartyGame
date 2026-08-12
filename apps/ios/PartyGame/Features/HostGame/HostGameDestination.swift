import SwiftUI

struct HostGameDestination: View {
    let store: GameSessionStore
    @State private var nickname = ""
    @State private var settings = RoomSettings()
    @State private var availablePackages: [ContentPackage] = []
    @State private var selectedPackageKeys: Set<String> = []
    @State private var selectedQuestionTypes: Set<String> = ["PlayerSelection"]
    @State private var isLoadingPackages = true

    var body: some View {
        Form {
            Section("player.section") {
                TextField("player.nickname", text: $nickname)
                    .textInputAutocapitalization(.words)
                    .accessibilityIdentifier("host.nickname")
            }
            
            Section("host.packages") {
                if isLoadingPackages {
                    ProgressView()
                } else if availablePackages.isEmpty {
                    Text("host.no_packages")
                } else {
                    ForEach(availablePackages) { pkg in
                        Button(action: {
                            if selectedPackageKeys.contains(pkg.key) {
                                selectedPackageKeys.remove(pkg.key)
                            } else {
                                selectedPackageKeys.insert(pkg.key)
                            }
                        }) {
                            HStack {
                                Text(pkg.name)
                                    .foregroundColor(.primary)
                                Spacer()
                                if selectedPackageKeys.contains(pkg.key) {
                                    Image(systemName: "checkmark")
                                        .foregroundColor(.blue)
                                }
                            }
                        }
                    }
                }
            }
            
            Section("host.game_settings") {
                Stepper(value: $settings.roundCount, in: 1 ... 10) { row("settings.rounds", settings.roundCount) }
                Stepper(value: $settings.questionsPerRound, in: 4 ... 6) { row("settings.questions", settings.questionsPerRound) }
                Stepper(value: $settings.playerSelectionSeconds, in: 5 ... 120, step: 5) { row("settings.selection", settings.playerSelectionSeconds) }
                Stepper(value: $settings.textAnswerSeconds, in: 5 ... 180, step: 5) { row("settings.text_answer", settings.textAnswerSeconds) }
                Stepper(value: $settings.votingSeconds, in: 5 ... 120, step: 5) { row("settings.voting", settings.votingSeconds) }
                Stepper(value: $settings.photoSeconds, in: 10 ... 180, step: 5) { row("settings.photo", settings.photoSeconds) }
                Stepper(value: $settings.drawingSeconds, in: 30 ... 300, step: 10) { row("settings.drawing", settings.drawingSeconds) }
                Stepper(value: $settings.resultPresentationSeconds, in: 3 ... 30) { row("settings.results", settings.resultPresentationSeconds) }
                Toggle("settings.final_enabled", isOn: $settings.finalRoundEnabled)
                if settings.finalRoundEnabled {
                    Stepper(value: $settings.finalDrawingPasses, in: 1 ... 9) { row("settings.final_passes", settings.finalDrawingPasses) }
                }
            }
            Section("host.question_types") {
                ForEach(Self.questionTypes, id: \.key) { questionType in
                    Toggle(isOn: Binding(
                        get: { selectedQuestionTypes.contains(questionType.key) },
                        set: { isSelected in
                            if isSelected {
                                selectedQuestionTypes.insert(questionType.key)
                            } else {
                                selectedQuestionTypes.remove(questionType.key)
                            }
                        }
                    )) {
                        Text(questionType.localizationKey)
                    }
                    .accessibilityIdentifier("host.question-type-\(questionType.key)")
                }
            }
            if let error = store.errorMessage { Text(error).foregroundStyle(.red) }
            Section {
                Button("host.create") {
                    Task {
                        await store.createRoom(
                            nickname: nickname,
                            settings: settings,
                            selectedPackageKeys: selectedPackageKeys.isEmpty ? nil : Array(selectedPackageKeys).sorted(),
                            enabledQuestionTypes: selectedQuestionTypes.sorted()
                        )
                    }
                }
                    .disabled(!valid || store.isWorking)
                    .accessibilityIdentifier("host.create")
                if store.isWorking { ProgressView() }
            }
        }
        .navigationTitle("host.title")
        .toolbar { ToolbarItem(placement: .topBarLeading) { Button("common.back") { store.showHome() }.accessibilityIdentifier("common.back") } }
        .task {
            do {
                availablePackages = try await store.fetchPackages()
            } catch {
                store.errorMessage = error.localizedDescription
            }
            isLoadingPackages = false
        }
    }

    private var valid: Bool {
        (2 ... 20).contains(nickname.trimmingCharacters(in: .whitespaces).count)
            && settings.isValid
            && !selectedQuestionTypes.isEmpty
    }
    private func row(_ key: LocalizedStringKey, _ value: Int) -> some View {
        HStack { Text(key); Spacer(); Text("\(value)").monospacedDigit() }
    }

    private static let questionTypes: [(key: String, localizationKey: LocalizedStringKey)] = [
        ("PlayerSelection", "question_type.player_selection"),
        ("TextAnswer", "question_type.text_answer"),
        ("PhotoAnswer", "question_type.photo_answer"),
        ("DrawingAnswer", "question_type.drawing_answer")
    ]
}
