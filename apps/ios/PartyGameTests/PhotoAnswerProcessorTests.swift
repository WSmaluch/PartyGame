import ImageIO
import UIKit
import XCTest
@testable import PartyGame

final class PhotoAnswerProcessorTests: XCTestCase {
    func testLargeImageResizesTo2048WithoutUpscaling() async throws {
        let result = try await PhotoAnswerProcessor().prepare(image: image(width: 4_000, height: 2_000))
        XCTAssertEqual(result.width, 2_048)
        XCTAssertEqual(result.height, 1_024)
        XCTAssertLessThanOrEqual(result.byteCount, PhotoAnswerProcessor.maximumBytes)
    }

    func testSmallImageIsNotUpscaled() async throws {
        let result = try await PhotoAnswerProcessor().prepare(image: image(width: 640, height: 480))
        XCTAssertEqual(result.width, 640)
        XCTAssertEqual(result.height, 480)
    }

    func testPNGAndJPEGInputBecomeJPEG() async throws {
        for data in [image(width: 700, height: 500).pngData()!, image(width: 700, height: 500).jpegData(compressionQuality: 1)!] {
            let result = try await PhotoAnswerProcessor().prepare(data: data)
            XCTAssertEqual(Array(result.jpegData.prefix(3)), [0xff, 0xd8, 0xff])
        }
    }

    func testPortraitAndLandscapePreserveAspectRatio() async throws {
        let portrait = try await PhotoAnswerProcessor().prepare(image: image(width: 900, height: 1_600))
        let landscape = try await PhotoAnswerProcessor().prepare(image: image(width: 1_600, height: 900))
        XCTAssertEqual(Double(portrait.width) / Double(portrait.height), 0.5625, accuracy: 0.01)
        XCTAssertEqual(Double(landscape.width) / Double(landscape.height), 1.777, accuracy: 0.01)
    }

    func testReencodingDoesNotCarryImagePropertiesMetadata() async throws {
        let result = try await PhotoAnswerProcessor().prepare(image: image(width: 800, height: 600))
        let source = CGImageSourceCreateWithData(result.jpegData as CFData, nil)!
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        XCTAssertNil(properties?[kCGImagePropertyGPSDictionary])
        let exif = properties?[kCGImagePropertyExifDictionary] as? [CFString: Any]
        XCTAssertNil(exif?[kCGImagePropertyExifUserComment])
        XCTAssertNil(exif?[kCGImagePropertyExifDateTimeOriginal])
    }

    func testCorruptedDataFailsCleanly() async {
        do { _ = try await PhotoAnswerProcessor().prepare(data: Data([0, 1, 2])); XCTFail("Expected failure") }
        catch { XCTAssertEqual(error as? PhotoAnswerProcessingError, .invalidImage) }
    }

    func testAsyncPreparationCompletesWithValidData() async throws {
        let result = try await PhotoAnswerProcessor().prepare(image: image(width: 1_000, height: 1_000))
        XCTAssertGreaterThan(result.byteCount, 0)
    }

    private func image(width: Int, height: Int) -> UIImage {
        let format = UIGraphicsImageRendererFormat(); format.scale = 1
        return UIGraphicsImageRenderer(size: CGSize(width: width, height: height), format: format).image { context in
            UIColor.systemPink.setFill(); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        }
    }
}
