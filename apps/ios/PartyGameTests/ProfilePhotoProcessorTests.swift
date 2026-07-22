import UIKit
import XCTest
@testable import PartyGame

final class ProfilePhotoProcessorTests: XCTestCase {
    func testResizesAndCompressesPhotoBelowUploadLimit() throws {
        let image = UIGraphicsImageRenderer(size: CGSize(width: 2_800, height: 1_800)).image { context in
            UIColor.systemPink.setFill()
            context.fill(CGRect(x: 0, y: 0, width: 2_800, height: 1_800))
        }
        let data = try ProfilePhotoProcessor().jpegData(from: image)
        let processed = try XCTUnwrap(UIImage(data: data))
        XCTAssertLessThanOrEqual(max(processed.size.width, processed.size.height), ProfilePhotoProcessor.maximumDimension)
        XCTAssertLessThanOrEqual(data.count, ProfilePhotoProcessor.maximumBytes)
    }
}
