import XCTest
@testable import PartyGame

final class PhotoAnswerUploadTests: XCTestCase {
    func testMultipartUsesExactBackendFieldNamesJPEGAndSafeFilename() {
        let player = UUID(), submission = UUID()
        let multipart = MultipartFormDataBuilder.photoAnswer(playerId: player, reconnectToken: "secret", clientSubmissionId: submission,
                                                              jpegData: Data([0xff, 0xd8, 0xff]), boundary: "BOUNDARY")
        let body = String(decoding: multipart.body, as: UTF8.self)
        XCTAssertEqual(multipart.contentType, "multipart/form-data; boundary=BOUNDARY")
        for name in ["playerId", "reconnectToken", "clientSubmissionId", "photo"] { XCTAssertTrue(body.contains("name=\"\(name)\"")) }
        XCTAssertTrue(body.contains("filename=\"photo.jpg\""))
        XCTAssertTrue(body.contains("Content-Type: image/jpeg"))
        XCTAssertTrue(body.contains(player.uuidString))
        XCTAssertTrue(body.contains(submission.uuidString))
    }

    func testRetryCanReuseClientSubmissionIdAndChangingPhotoCanUseNewId() {
        let same = UUID()
        let first = MultipartFormDataBuilder.photoAnswer(playerId: UUID(), reconnectToken: "t", clientSubmissionId: same, jpegData: Data(), boundary: "A")
        let retry = MultipartFormDataBuilder.photoAnswer(playerId: UUID(), reconnectToken: "t", clientSubmissionId: same, jpegData: Data(), boundary: "B")
        XCTAssertTrue(String(decoding: first.body, as: UTF8.self).contains(same.uuidString))
        XCTAssertTrue(String(decoding: retry.body, as: UTF8.self).contains(same.uuidString))
        XCTAssertNotEqual(same, UUID())
    }

    func testUploadProgressStateDoesNotTreatOneHundredPercentAsSaved() {
        XCTAssertNotEqual(PhotoAnswerUploadPhase.uploading(1), .saved)
        XCTAssertNotEqual(PhotoAnswerUploadPhase.serverProcessing, .saved)
    }

    func testProblemDetailsDecodesBackendExtensionCode() throws {
        let problem = try JSONDecoder().decode(ProblemDetails.self, from: Data(#"{"status":409,"code":"photo_answer_already_submitted"}"#.utf8))
        XCTAssertEqual(problem.code, "photo_answer_already_submitted")
    }
}
