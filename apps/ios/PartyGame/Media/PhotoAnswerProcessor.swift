import UIKit

enum PhotoAnswerProcessingError: LocalizedError, Equatable {
    case invalidImage
    case cannotEncode
    case cancelled
    case tooLarge

    var errorDescription: String? {
        switch self {
        case .invalidImage, .cannotEncode: String(localized: "photoAnswer.error.invalidImage")
        case .cancelled: String(localized: "error.request_cancelled")
        case .tooLarge: String(localized: "photoAnswer.error.fileTooLarge")
        }
    }
}

struct PreparedPhotoAnswer: Equatable, Sendable {
    let jpegData: Data
    let width: Int
    let height: Int
    let byteCount: Int
}

struct PhotoAnswerProcessor: Sendable {
    static let maximumDimension: CGFloat = 2_048
    static let maximumBytes = 5 * 1_024 * 1_024

    func prepare(image: UIImage) async throws -> PreparedPhotoAnswer {
        try await Task.detached(priority: .userInitiated) {
            guard !Task.isCancelled else { throw PhotoAnswerProcessingError.cancelled }
            return try autoreleasepool {
                let sourceSize = image.size
                guard sourceSize.width > 0, sourceSize.height > 0 else { throw PhotoAnswerProcessingError.invalidImage }
                let scale = min(1, Self.maximumDimension / max(sourceSize.width, sourceSize.height))
                let target = CGSize(width: max(1, floor(sourceSize.width * scale)), height: max(1, floor(sourceSize.height * scale)))
                let format = UIGraphicsImageRendererFormat()
                format.scale = 1
                format.opaque = true
                let normalized = UIGraphicsImageRenderer(size: target, format: format).image { context in
                    UIColor.black.setFill()
                    context.fill(CGRect(origin: .zero, size: target))
                    image.draw(in: CGRect(origin: .zero, size: target))
                }
                guard !Task.isCancelled else { throw PhotoAnswerProcessingError.cancelled }
                for quality in stride(from: 0.86, through: 0.62, by: -0.06) {
                    guard let data = normalized.jpegData(compressionQuality: quality) else { continue }
                    if data.count <= Self.maximumBytes {
                        return PreparedPhotoAnswer(jpegData: data, width: Int(target.width), height: Int(target.height), byteCount: data.count)
                    }
                }
                throw PhotoAnswerProcessingError.tooLarge
            }
        }.value
    }

    func prepare(data: Data) async throws -> PreparedPhotoAnswer {
        guard let image = UIImage(data: data) else { throw PhotoAnswerProcessingError.invalidImage }
        return try await prepare(image: image)
    }
}

struct PhotoAnswerDraft: Equatable, Sendable {
    let roomCode: String
    let playerId: UUID
    let questionInstanceId: UUID
    let clientSubmissionId: UUID
    let fileURL: URL
    let previewJPEG: Data
    let width: Int
    let height: Int
    let byteCount: Int

    var key: String { "\(roomCode):\(playerId.uuidString):\(questionInstanceId.uuidString)" }
}

enum PhotoAnswerUploadPhase: Equatable, Sendable {
    case idle, preparing, ready, uploading(Double), serverProcessing, saved, failed(String)
}

enum PhotoAnswerDraftStorage {
    private static var directory: URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("PartyGamePhotoAnswerDrafts", isDirectory: true)
    }

    static func save(_ prepared: PreparedPhotoAnswer, roomCode: String, playerId: UUID, questionInstanceId: UUID, submissionId: UUID) throws -> PhotoAnswerDraft {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let fileURL = directory.appendingPathComponent("\(roomCode)-\(playerId.uuidString)-\(questionInstanceId.uuidString)-\(submissionId.uuidString).jpg")
        try prepared.jpegData.write(to: fileURL, options: .atomic)
        let preview = UIImage(data: prepared.jpegData)?.preparingThumbnail(of: CGSize(width: 420, height: 420))?.jpegData(compressionQuality: 0.72) ?? Data()
        return PhotoAnswerDraft(roomCode: roomCode, playerId: playerId, questionInstanceId: questionInstanceId,
                                clientSubmissionId: submissionId, fileURL: fileURL, previewJPEG: preview,
                                width: prepared.width, height: prepared.height, byteCount: prepared.byteCount)
    }

    static func remove(_ draft: PhotoAnswerDraft) { try? FileManager.default.removeItem(at: draft.fileURL) }

    static func cleanup(olderThan age: TimeInterval = 24 * 60 * 60) {
        guard let urls = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.contentModificationDateKey]) else { return }
        let cutoff = Date().addingTimeInterval(-age)
        for url in urls where (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantFuture < cutoff {
            try? FileManager.default.removeItem(at: url)
        }
    }
}
