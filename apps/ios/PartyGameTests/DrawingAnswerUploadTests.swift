import XCTest
@testable import PartyGame

final class DrawingAnswerUploadTests: XCTestCase {
    func testMultipartUsesBackendDrawingFieldPNGAndNoClientFilename() {
        let player = UUID(), submission = UUID()
        let multipart = MultipartFormDataBuilder.drawingAnswer(playerId: player, reconnectToken: "secret", clientSubmissionId: submission, pngData: Data([137, 80, 78, 71]), boundary: "BOUNDARY")
        let body = String(decoding: multipart.body, as: UTF8.self)
        XCTAssertEqual(multipart.contentType, "multipart/form-data; boundary=BOUNDARY")
        for name in ["playerId", "reconnectToken", "clientSubmissionId", "drawing"] { XCTAssertTrue(body.contains("name=\"\(name)\"")) }
        XCTAssertTrue(body.contains("filename=\"drawing.png\""))
        XCTAssertTrue(body.contains("Content-Type: image/png"))
        XCTAssertFalse(body.contains("original-file-name"))
    }

    func testRetryKeepsTheSameSubmissionIdentifier() {
        let id = UUID()
        let first = MultipartFormDataBuilder.drawingAnswer(playerId: UUID(), reconnectToken: "t", clientSubmissionId: id, pngData: Data(), boundary: "A")
        let retry = MultipartFormDataBuilder.drawingAnswer(playerId: UUID(), reconnectToken: "t", clientSubmissionId: id, pngData: Data(), boundary: "B")
        XCTAssertTrue(String(decoding: first.body, as: UTF8.self).contains(id.uuidString))
        XCTAssertTrue(String(decoding: retry.body, as: UTF8.self).contains(id.uuidString))
    }
}
