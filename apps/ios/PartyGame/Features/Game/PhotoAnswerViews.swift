import AVFoundation
import PhotosUI
import SwiftUI
import UIKit

struct PhotoAnswerCaptureView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    @Environment(\.openURL) private var openURL
    @State private var pickerItem: PhotosPickerItem?
    @State private var showsCamera = false
    @State private var cameraMessage: String?

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                PhotoAnswerTaskHeader(game: game)
                Text(String(format: String(localized: "photoAnswer.submissionProgress"), game.photoAnswerResults?.submittedPlayers ?? 0,
                            game.photoAnswerResults?.requiredPlayers ?? 0))
                    .font(.headline)

                if let draft = store.photoDraft, draft.questionInstanceId == game.resolvedQuestionInstanceId {
                    PhotoAnswerPreview(store: store, draft: draft, pickerItem: $pickerItem, showsCamera: $showsCamera)
                } else if store.photoUploadPhase == .preparing {
                    ProgressView(String(localized: "photoAnswer.preparing"))
                } else {
                    Button("photoAnswer.takePhoto", systemImage: "camera") { requestCamera() }
                        .buttonStyle(.borderedProminent).controlSize(.large).accessibilityIdentifier("photoAnswer.takePhoto")
                    PhotosPicker(selection: $pickerItem, matching: .images) {
                        Label("photoAnswer.chooseLibrary", systemImage: "photo.on.rectangle")
                    }
                    .buttonStyle(.bordered).controlSize(.large).accessibilityIdentifier("photoAnswer.chooseLibrary")
                }
                if let cameraMessage {
                    Text(cameraMessage).foregroundStyle(.secondary)
                    Button("photoAnswer.openSettings") {
                        if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
                    }
                }
                if let error = store.errorMessage { Text(error).foregroundStyle(.red).accessibilityIdentifier("photoAnswer.error") }
            }.padding()
        }
        .sheet(isPresented: $showsCamera) {
            CameraCaptureView(onImage: { image in
                showsCamera = false
                Task { await store.preparePhotoAnswer(image) }
            }, onCancel: { showsCamera = false }, cameraDevice: .rear)
        }
        .onChange(of: pickerItem) { _, item in
            guard let item else { return }
            Task {
                do {
                    guard let data = try await item.loadTransferable(type: Data.self), let image = UIImage(data: data) else {
                        throw PhotoAnswerProcessingError.invalidImage
                    }
                    await store.preparePhotoAnswer(image)
                } catch { store.errorMessage = error.localizedDescription }
                pickerItem = nil
            }
        }
    }

    private func requestCamera() {
        guard !store.isPhotoCameraUnavailableFixture, UIImagePickerController.isSourceTypeAvailable(.camera) else {
            cameraMessage = String(localized: "photoAnswer.cameraUnavailable")
            return
        }
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized: showsCamera = true
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { allowed in
                Task { @MainActor in
                    showsCamera = allowed
                    if !allowed { cameraMessage = String(localized: "photoAnswer.cameraDenied") }
                }
            }
        case .denied, .restricted: cameraMessage = String(localized: "photoAnswer.cameraDenied")
        @unknown default: cameraMessage = String(localized: "photoAnswer.cameraUnavailable")
        }
    }
}

private struct PhotoAnswerPreview: View {
    let store: GameSessionStore
    let draft: PhotoAnswerDraft
    @Binding var pickerItem: PhotosPickerItem?
    @Binding var showsCamera: Bool

    var body: some View {
        VStack(spacing: 16) {
            if let image = UIImage(data: draft.previewJPEG) {
                Image(uiImage: image).resizable().scaledToFit().clipShape(RoundedRectangle(cornerRadius: 18))
                    .accessibilityLabel("photoAnswer.preview")
            }
            Text(ByteCountFormatter.string(fromByteCount: Int64(draft.byteCount), countStyle: .file)).foregroundStyle(.secondary)
            switch store.photoUploadPhase {
            case .uploading(let value):
                ProgressView(value: value).accessibilityValue("\(Int(value * 100))%")
                Text(String(format: String(localized: "photoAnswer.uploadingPercent"), Int(value * 100)))
                Button("common.cancel") { store.cancelPhotoAnswerUpload() }
            case .serverProcessing: ProgressView(String(localized: "photoAnswer.saving"))
            case .failed:
                Button("common.retry") { store.uploadPhotoAnswer() }.buttonStyle(.borderedProminent)
                Button("photoAnswer.chooseOther") { store.discardPhotoAnswerDraft() }
            default:
                Button("photoAnswer.usePhoto") { store.uploadPhotoAnswer() }
                    .buttonStyle(.borderedProminent).accessibilityIdentifier("photoAnswer.usePhoto")
                HStack {
                    Button("photoAnswer.takeOther") { store.discardPhotoAnswerDraft(); showsCamera = true }
                    PhotosPicker(selection: $pickerItem, matching: .images) { Text("photoAnswer.chooseOther") }
                }
            }
        }
    }
}

struct PhotoAnswerWaitingView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View {
        VStack(spacing: 18) {
            PhotoAnswerTaskHeader(game: game)
            Image(systemName: "checkmark.circle.fill").font(.system(size: 62)).foregroundStyle(.green)
            Text("photoAnswer.sent").font(.title.bold())
            if let data = store.photoDraft?.previewJPEG, let image = UIImage(data: data) {
                Image(uiImage: image).resizable().scaledToFit().frame(maxHeight: 240).clipShape(RoundedRectangle(cornerRadius: 16))
            } else { Text("photoAnswer.savedConfirmation") }
            Text(String(format: String(localized: "photoAnswer.submissionProgress"), game.photoAnswerResults?.submittedPlayers ?? 0,
                        game.photoAnswerResults?.requiredPlayers ?? 0))
            Text("photoAnswer.waitingPlayers").foregroundStyle(.secondary)
        }.accessibilityIdentifier("photoAnswer.waiting")
    }
}

struct PhotoAnswerRevealWaitingView: View {
    let game: GameSnapshot
    var body: some View { VStack(spacing: 18) { PhotoAnswerTaskHeader(game: game); ProgressView(); Text("photoAnswer.revealOnDisplay").font(.title2) } }
}

struct PhotoAnswerVotingView: View {
    @Bindable var store: GameSessionStore
    let game: GameSnapshot
    @State private var fullScreenOption: AnonymousPhotoAnswer?
    @State private var localSelection: UUID?

    var body: some View {
        VStack(spacing: 14) {
            PhotoAnswerTaskHeader(game: game)
            Text("photoAnswer.choosePhoto").font(.headline)
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 145), spacing: 14)], spacing: 14) {
                    ForEach((game.photoAnswerResults?.anonymousOptions ?? []).sorted { $0.displayOrder < $1.displayOrder }) { option in
                        let own = option.photoAnswerId == store.privateGameState?.ownPhotoAnswerId
                        let selected = option.photoAnswerId == (localSelection ?? store.selectedPhotoAnswerVoteId)
                        VStack {
                            PhotoAnswerRemoteImage(url: store.mediaURL(option.thumbnailPhotoUrl),
                                accessibilityLabel: photoLabel(option, own: own, selected: selected))
                                .frame(minHeight: 150)
                            HStack { Text("#\(option.displayOrder + 1)"); if own { Text("photoAnswer.ownPhoto") }; if selected { Image(systemName: "checkmark.circle.fill") } }
                        }
                        .padding(8)
                        .contentShape(Rectangle())
                        .onTapGesture { choose(option, selected: selected) }
                        .accessibilityElement(children: .combine)
                        .accessibilityAddTraits(.isButton)
                        .accessibilityAction { choose(option, selected: selected) }
                        .accessibilityIdentifier("photoAnswer.option.\(option.photoAnswerId.uuidString)")
                        .overlay(RoundedRectangle(cornerRadius: 16).stroke(selected ? Color.accentColor : .secondary, lineWidth: selected ? 4 : 1))
                    }
                }.padding(4)
            }
            Button("photoAnswer.vote") { Task { await store.submitSelectedPhotoAnswerVote() } }
                .buttonStyle(.borderedProminent)
                .disabled((localSelection ?? store.selectedPhotoAnswerVoteId) == nil || store.isWorking)
                .accessibilityIdentifier("photoAnswer.vote")
        }
        .sheet(item: $fullScreenOption) { option in
            PhotoAnswerRemoteImage(url: store.mediaURL(option.displayPhotoUrl), accessibilityLabel: "photoAnswer.fullPhoto")
                .padding().presentationDetents([.large])
        }
    }

    private func photoLabel(_ option: AnonymousPhotoAnswer, own: Bool, selected: Bool) -> String {
        var value = String(format: String(localized: "photoAnswer.numberedPhoto"), option.displayOrder + 1)
        if own { value += ". " + String(localized: "photoAnswer.ownPhoto") }
        if selected { value += ". " + String(localized: "photoAnswer.selected") }
        return value
    }

    private func choose(_ option: AnonymousPhotoAnswer, selected: Bool) {
        if selected { fullScreenOption = option }
        else {
            localSelection = option.photoAnswerId
            store.selectPhotoAnswerVote(option.photoAnswerId)
        }
    }
}

struct PhotoAnswerVoteWaitingView: View {
    let game: GameSnapshot
    var body: some View { VStack(spacing: 18) { PhotoAnswerTaskHeader(game: game); Image(systemName: "checkmark.circle.fill").font(.system(size: 62)); Text("photoAnswer.voteSaved").font(.title.bold()); Text("photoAnswer.waitingPlayers") } }
}

struct PhotoAnswerResultsView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View {
        VStack { PhotoAnswerTaskHeader(game: game)
            let options = game.photoAnswerResults?.options ?? []
            if options.isEmpty { ContentUnavailableView("photoAnswer.nobodySubmitted", systemImage: "camera") }
            else { ScrollView { VStack(spacing: 18) { ForEach(options) { option in
                VStack(alignment: .leading, spacing: 10) {
                    if option.isTopResult {
                        Label("photoAnswer.topVotes", systemImage: "trophy.fill")
                            .foregroundStyle(.yellow).font(.headline)
                            .accessibilityIdentifier("photoAnswer.topResult")
                    }
                    PhotoAnswerRemoteImage(url: store.mediaURL(option.displayPhotoUrl), accessibilityLabel: option.authorNickname)
                    HStack(spacing: 10) {
                        PhotoAnswerRemoteImage(url: store.mediaURL(option.authorPhotoUrl), accessibilityLabel: option.authorNickname)
                            .frame(width: 38, height: 38).clipShape(Circle())
                        Text("\(String(localized: "photoAnswer.author")): \(option.authorNickname)").font(.headline)
                    }
                    Text("\(option.voteCount) \(String(localized: "photoAnswer.votes"))")
                    if !option.voters.isEmpty {
                        Text("photoAnswer.voters").font(.headline)
                        ForEach(option.voters) { voter in
                            HStack {
                                PhotoAnswerRemoteImage(url: store.mediaURL(voter.profilePhotoUrl), accessibilityLabel: voter.nickname)
                                    .frame(width: 30, height: 30).clipShape(Circle())
                                Text(voter.nickname)
                                Spacer()
                                Text("+\(voter.pointsAwarded) pkt").bold()
                            }
                        }
                    }
                }.padding().background(.secondary.opacity(0.1), in: RoundedRectangle(cornerRadius: 18))
            } }.padding(.vertical) } }
        }
    }
}

struct PhotoAnswerPrivateStateLoader: View { var body: some View { ProgressView().accessibilityLabel("photoAnswer.loadingPrivateState") } }

private struct PhotoAnswerTaskHeader: View {
    let game: GameSnapshot
    var body: some View {
        VStack(spacing: 8) {
            Text("\(String(localized: "round.summary.title")) \(game.currentRoundNumber) · \(game.currentQuestionNumber)/\(game.questionsInCurrentRound)").font(.caption)
            Text(game.categories?.first?.name ?? "").foregroundStyle(.secondary)
            Text(game.currentQuestion?.questionText.local ?? "").font(.title2.bold()).multilineTextAlignment(.center)
        }
    }
}
