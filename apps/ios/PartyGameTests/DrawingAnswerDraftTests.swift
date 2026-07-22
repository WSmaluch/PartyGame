import XCTest
@testable import PartyGame

final class DrawingAnswerDraftTests: XCTestCase {
    private var drafts: [DrawingAnswerDraft] = []

    override func tearDown() {
        drafts.forEach(DrawingAnswerDraftStorage.remove)
        drafts.removeAll()
        super.tearDown()
    }

    func testDraftPersistsCanvasPNGAndStableRetryIdentifier() throws {
        var draft = makeDraft(roomCode: "R\(UUID().uuidString.prefix(5))")
        draft.canvas.complete([DrawingPoint(x: 0.1, y: 0.2), DrawingPoint(x: 0.8, y: 0.7)])
        draft.clientSubmissionId = UUID()
        let png = Data([137, 80, 78, 71, 13, 10, 26, 10])
        draft.pngURL = try DrawingAnswerDraftStorage.savePNG(png, for: draft)
        draft.previewPNG = png
        try DrawingAnswerDraftStorage.save(draft)
        drafts.append(draft)

        let restored = try XCTUnwrap(DrawingAnswerDraftStorage.load(roomCode: draft.roomCode,
            playerId: draft.playerId, questionInstanceId: draft.questionInstanceId))
        XCTAssertEqual(restored.canvas, draft.canvas)
        XCTAssertEqual(restored.clientSubmissionId, draft.clientSubmissionId)
        XCTAssertEqual(restored.previewPNG, png)
        XCTAssertEqual(try Data(contentsOf: XCTUnwrap(restored.pngURL)), png)
    }

    func testDraftIsIsolatedByRoomPlayerAndQuestion() throws {
        let draft = makeDraft(roomCode: "R\(UUID().uuidString.prefix(5))")
        try DrawingAnswerDraftStorage.save(draft)
        drafts.append(draft)

        XCTAssertNil(DrawingAnswerDraftStorage.load(roomCode: "OTHER", playerId: draft.playerId,
            questionInstanceId: draft.questionInstanceId))
        XCTAssertNil(DrawingAnswerDraftStorage.load(roomCode: draft.roomCode, playerId: UUID(),
            questionInstanceId: draft.questionInstanceId))
        XCTAssertNil(DrawingAnswerDraftStorage.load(roomCode: draft.roomCode, playerId: draft.playerId,
            questionInstanceId: UUID()))
    }

    func testEditingClearsRenderedPayloadAndUsesNewRetryIdentifier() {
        var draft = makeDraft(roomCode: "R\(UUID().uuidString.prefix(5))")
        let firstIdentifier = UUID()
        draft.clientSubmissionId = firstIdentifier
        draft.previewPNG = Data([1])
        draft.pngURL = URL(fileURLWithPath: "/private/tmp/old-drawing.png")
        draft.canvas.complete([DrawingPoint(x: 0.2, y: 0.2)])

        draft.previewPNG = nil
        draft.pngURL = nil
        draft.clientSubmissionId = nil
        let nextIdentifier = UUID()
        XCTAssertNotEqual(firstIdentifier, nextIdentifier)
        XCTAssertNil(draft.previewPNG)
        XCTAssertNil(draft.pngURL)
        XCTAssertNil(draft.clientSubmissionId)
    }

    func testRemoveDeletesPersistedDraftAndPNG() throws {
        var draft = makeDraft(roomCode: "R\(UUID().uuidString.prefix(5))")
        draft.pngURL = try DrawingAnswerDraftStorage.savePNG(Data([1, 2, 3]), for: draft)
        try DrawingAnswerDraftStorage.save(draft)
        DrawingAnswerDraftStorage.remove(draft)

        XCTAssertNil(DrawingAnswerDraftStorage.load(roomCode: draft.roomCode, playerId: draft.playerId,
            questionInstanceId: draft.questionInstanceId))
        XCTAssertFalse(FileManager.default.fileExists(atPath: try XCTUnwrap(draft.pngURL).path))
    }

    private func makeDraft(roomCode: String) -> DrawingAnswerDraft {
        DrawingAnswerDraft(roomCode: roomCode, playerId: UUID(), questionInstanceId: UUID(),
            canvas: DrawingCanvasState(), clientSubmissionId: nil, pngURL: nil, previewPNG: nil)
    }
}
