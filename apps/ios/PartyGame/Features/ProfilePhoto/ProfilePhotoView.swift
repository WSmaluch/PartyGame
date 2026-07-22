import AVFoundation
import PhotosUI
import SwiftUI
import UIKit

struct ProfilePhotoView: View {
    let store: GameSessionStore
    @State private var capturedImage: UIImage?
    @State private var isCameraPresented = false
    @State private var selectedLibraryItem: PhotosPickerItem?
    @State private var localError: String?

    var body: some View {
        VStack(spacing: 22) {
            Text("photo.title").font(.largeTitle.bold())
            Text("photo.explanation").multilineTextAlignment(.center).foregroundStyle(.secondary)

            Group {
                if let capturedImage {
                    Image(uiImage: capturedImage)
                        .resizable().scaledToFill()
                } else {
                    Image(systemName: "person.crop.circle.badge.camera")
                        .resizable().scaledToFit().padding(44).foregroundStyle(.purple)
                }
            }
            .frame(width: 230, height: 230)
            .background(.thinMaterial)
            .clipShape(Circle())
            .accessibilityLabel("photo.preview")
            .accessibilityIdentifier("profile-photo-preview")

            if let message = localError ?? store.errorMessage {
                Text(message).foregroundStyle(.red).multilineTextAlignment(.center)
                    .accessibilityIdentifier("profile-photo-required-error")
            }

            if capturedImage == nil {
                HStack {
                    Button("photo.open_camera") { requestCamera() }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("take-profile-photo-button")
                    PhotosPicker(selection: $selectedLibraryItem, matching: .images) {
                        Label("Wybierz z galerii", systemImage: "photo.on.rectangle")
                    }
                    .buttonStyle(.borderedProminent)
                    .accessibilityIdentifier("choose-profile-photo-button")
                }
                .accessibilityIdentifier("profile-photo-actions")
            } else {
                HStack {
                    Button("photo.retake") { requestCamera() }.buttonStyle(.bordered)
                    Button("photo.use") { upload() }
                        .buttonStyle(.borderedProminent)
                        .disabled(store.isWorking)
                        .accessibilityIdentifier("save-profile-button")
                }
            }

            if store.isWorking { ProgressView("photo.uploading") }
        }
        .padding(28)
        .fullScreenCover(isPresented: $isCameraPresented) {
            CameraCaptureView(
                onImage: { image in capturedImage = image; isCameraPresented = false },
                onCancel: { isCameraPresented = false }
            )
            .ignoresSafeArea()
        }
        .onChange(of: selectedLibraryItem) { _, item in
            guard let item else { return }
            Task { await loadLibraryImage(item) }
        }
        .navigationBarBackButtonHidden()
    }

    private func requestCamera() {
        localError = nil
        guard UIImagePickerController.isSourceTypeAvailable(.camera) else {
            localError = String(localized: "photo.error.camera_unavailable")
            return
        }
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized: isCameraPresented = true
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { allowed in
                Task { @MainActor in
                    if allowed { isCameraPresented = true }
                    else { localError = String(localized: "photo.error.permission") }
                }
            }
        default: localError = String(localized: "photo.error.permission")
        }
    }

    @MainActor
    private func loadLibraryImage(_ item: PhotosPickerItem) async {
        localError = nil
        do {
            guard let data = try await item.loadTransferable(type: Data.self),
                  let image = UIImage(data: data) else {
                localError = "Nie udało się odczytać wybranego zdjęcia."
                return
            }
            capturedImage = image
        } catch {
            localError = error.localizedDescription
        }
    }

    private func upload() {
        guard let capturedImage else { return }
        do {
            let data = try ProfilePhotoProcessor().jpegData(from: capturedImage)
            Task { await store.uploadProfilePhoto(data) }
        } catch { localError = error.localizedDescription }
    }
}
