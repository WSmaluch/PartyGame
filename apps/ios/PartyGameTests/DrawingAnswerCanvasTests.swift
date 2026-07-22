import XCTest
import ImageIO
import UIKit
@testable import PartyGame

@MainActor
final class DrawingAnswerCanvasTests: XCTestCase {
    func testUndoRedoAndClearPreserveExpectedStrokes() {
        var canvas = DrawingCanvasState()
        canvas.complete([DrawingPoint(x: 0.1, y: 0.1), DrawingPoint(x: 0.9, y: 0.9)])
        canvas.complete([DrawingPoint(x: 0.2, y: 0.2)])
        canvas.undo()
        XCTAssertEqual(canvas.completedStrokes.count, 1)
        XCTAssertEqual(canvas.redoStack.count, 1)
        canvas.redo()
        XCTAssertEqual(canvas.completedStrokes.count, 2)
        canvas.clear()
        XCTAssertTrue(canvas.isEmpty)
        XCTAssertEqual(canvas.redoStack.count, 2)
    }

    func testRendererCreatesOpaqueWhite1024SquarePNG() async throws {
        var canvas = DrawingCanvasState()
        canvas.selectedColor = .red
        canvas.selectedLineWidth = .thin
        canvas.complete([DrawingPoint(x: 0.15, y: 0.15), DrawingPoint(x: 0.85, y: 0.85)])
        let data = try await DrawingRenderer().render(canvas)
        XCTAssertEqual(Array(data.prefix(8)), [137, 80, 78, 71, 13, 10, 26, 10])
        let image = try XCTUnwrap(UIImage(data: data))
        XCTAssertEqual(image.size, DrawingRenderer.logicalSize)
        let pixel = try XCTUnwrap(image.cgImage?.dataProvider?.data)
        XCTAssertFalse((pixel as Data).isEmpty)
    }

    func testEmptyCanvasCannotBeRendered() async {
        await XCTAssertThrowsErrorAsync(try await DrawingRenderer().render(DrawingCanvasState()))
    }

    func testBrushEraserColorWidthAndUndoAfterClear() {
        var canvas = DrawingCanvasState()
        canvas.selectedColor = .purple
        canvas.selectedLineWidth = .thick
        canvas.complete([DrawingPoint(x: 0.25, y: 0.25)])
        XCTAssertEqual(canvas.completedStrokes.last?.color, .purple)
        XCTAssertEqual(canvas.completedStrokes.last?.lineWidth, DrawingLineWidth.thick.rawValue)
        canvas.selectedTool = .eraser
        canvas.complete([DrawingPoint(x: 0.5, y: 0.5)])
        XCTAssertEqual(canvas.completedStrokes.last?.tool, .eraser)
        canvas.clear()
        XCTAssertTrue(canvas.isEmpty)
        canvas.undo()
        XCTAssertTrue(canvas.isEmpty)
        canvas.redo()
        XCTAssertFalse(canvas.isEmpty)
    }

    func testRendererProducesPNGWithWhiteBackgroundAndWorkingEraser() async throws {
        var canvas = DrawingCanvasState()
        canvas.selectedColor = .black
        canvas.selectedLineWidth = .thick
        canvas.complete([DrawingPoint(x: 0.1, y: 0.5), DrawingPoint(x: 0.9, y: 0.5)])
        canvas.selectedTool = .eraser
        canvas.complete([DrawingPoint(x: 0.5, y: 0.45), DrawingPoint(x: 0.5, y: 0.55)])

        let data = try await DrawingRenderer().render(canvas)
        let source = try XCTUnwrap(CGImageSourceCreateWithData(data as CFData, nil))
        XCTAssertEqual(CGImageSourceGetType(source) as String?, "public.png")
        let properties = try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [String: Any])
        XCTAssertEqual(properties[kCGImagePropertyPixelWidth as String] as? Int, 1024)
        let exif = properties[kCGImagePropertyExifDictionary as String] as? [String: Any] ?? [:]
        XCTAssertNil(exif[kCGImagePropertyExifUserComment as String])
        XCTAssertNil(properties[kCGImagePropertyIPTCDictionary as String])
        XCTAssertNil(properties[kCGImagePropertyGPSDictionary as String])
    }
}

private func XCTAssertThrowsErrorAsync<T>(_ expression: @autoclosure () async throws -> T, file: StaticString = #filePath, line: UInt = #line) async {
    do { _ = try await expression(); XCTFail("Expected error", file: file, line: line) }
    catch { }
}
