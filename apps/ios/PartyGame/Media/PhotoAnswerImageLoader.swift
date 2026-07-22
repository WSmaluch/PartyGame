import SwiftUI
import UIKit

actor PhotoAnswerImageCache {
    static let shared = PhotoAnswerImageCache()
    private let cache = NSCache<NSURL, UIImage>()
    private var tasks: [URL: Task<UIImage, Error>] = [:]

    func image(for url: URL) async throws -> UIImage {
        if let cached = cache.object(forKey: url as NSURL) { return cached }
        if let task = tasks[url] { return try await task.value }
        let task = Task<UIImage, Error> {
            let (data, response) = try await URLSession.shared.data(from: url)
            guard let http = response as? HTTPURLResponse, (200 ... 299).contains(http.statusCode),
                  let image = UIImage(data: data) else { throw URLError(.cannotDecodeContentData) }
            return image
        }
        tasks[url] = task
        defer { tasks[url] = nil }
        let image = try await task.value
        cache.setObject(image, forKey: url as NSURL)
        return image
    }

    func clear() {
        tasks.values.forEach { $0.cancel() }
        tasks.removeAll()
        cache.removeAllObjects()
    }
}

struct PhotoAnswerRemoteImage: View {
    let url: URL?
    let accessibilityLabel: String
    @State private var image: UIImage?
    @State private var failed = false

    var body: some View {
        Group {
            if let image { Image(uiImage: image).resizable().scaledToFit() }
            else if failed {
                ContentUnavailableView("photoAnswer.imageUnavailable", systemImage: "photo.badge.exclamationmark")
            } else { ProgressView().frame(minHeight: 160) }
        }
        .accessibilityLabel(accessibilityLabel)
        .task(id: url) {
            image = nil; failed = false
            guard let url else { failed = true; return }
            do { image = try await PhotoAnswerImageCache.shared.image(for: url) }
            catch is CancellationError { }
            catch { failed = true }
        }
    }
}
