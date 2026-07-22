import UIKit

enum ProfilePhotoProcessingError: LocalizedError {
    case cannotEncode
    case tooLarge

    var errorDescription: String? {
        switch self {
        case .cannotEncode: String(localized: "photo.error.processing")
        case .tooLarge: String(localized: "photo.error.too_large")
        }
    }
}

struct ProfilePhotoProcessor {
    static let maximumDimension: CGFloat = 1_200
    static let maximumBytes = 5 * 1_024 * 1_024

    func jpegData(from image: UIImage) throws -> Data {
        let normalized = normalizedImage(image)
        let scale = min(1, Self.maximumDimension / max(normalized.size.width, normalized.size.height))
        let size = CGSize(width: normalized.size.width * scale, height: normalized.size.height * scale)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let renderer = UIGraphicsImageRenderer(size: size, format: format)
        let resized = renderer.image { _ in normalized.draw(in: CGRect(origin: .zero, size: size)) }

        for quality in stride(from: 0.86, through: 0.45, by: -0.08) {
            guard let data = resized.jpegData(compressionQuality: quality) else { continue }
            if data.count <= Self.maximumBytes { return data }
        }
        throw ProfilePhotoProcessingError.tooLarge
    }

    private func normalizedImage(_ image: UIImage) -> UIImage {
        guard image.imageOrientation != .up else { return image }
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let renderer = UIGraphicsImageRenderer(size: image.size, format: format)
        return renderer.image { _ in image.draw(in: CGRect(origin: .zero, size: image.size)) }
    }
}
