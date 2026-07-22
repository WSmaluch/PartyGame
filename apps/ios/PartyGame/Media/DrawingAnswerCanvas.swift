import Foundation
import SwiftUI
import UIKit

enum DrawingTool: String, Codable, Sendable { case brush, eraser }

enum DrawingColor: String, CaseIterable, Codable, Sendable {
    case black, white, red, blue, green, yellow, orange, purple, pink, brown

    var color: Color {
        switch self {
        case .black: .black; case .white: .white; case .red: .red; case .blue: .blue
        case .green: .green; case .yellow: .yellow; case .orange: .orange; case .purple: .purple
        case .pink: .pink; case .brown: .brown
        }
    }

    var uiColor: UIColor { UIColor(color) }
    var accessibilityName: LocalizedStringKey { LocalizedStringKey("drawing.color.\(rawValue)") }
}

enum DrawingLineWidth: CGFloat, CaseIterable, Codable, Sendable {
    case thin = 4, medium = 10, thick = 22
    var label: LocalizedStringKey { LocalizedStringKey("drawing.width.\(self == .thin ? "thin" : self == .medium ? "medium" : "thick")") }
}

struct DrawingPoint: Codable, Equatable, Sendable { var x: CGFloat; var y: CGFloat }

struct DrawingStroke: Identifiable, Codable, Equatable, Sendable {
    let id: UUID
    var points: [DrawingPoint]
    var color: DrawingColor
    var lineWidth: CGFloat
    var tool: DrawingTool
}

struct DrawingCanvasState: Codable, Equatable, Sendable {
    var completedStrokes: [DrawingStroke] = []
    var redoStack: [DrawingStroke] = []
    var selectedColor: DrawingColor = .black
    var selectedLineWidth: DrawingLineWidth = .medium
    var selectedTool: DrawingTool = .brush

    var isEmpty: Bool { completedStrokes.isEmpty }
    mutating func complete(_ points: [DrawingPoint]) {
        guard !points.isEmpty else { return }
        completedStrokes.append(DrawingStroke(id: UUID(), points: points, color: selectedColor, lineWidth: selectedLineWidth.rawValue, tool: selectedTool))
        redoStack.removeAll()
    }
    mutating func undo() { guard let stroke = completedStrokes.popLast() else { return }; redoStack.append(stroke) }
    mutating func redo() { guard let stroke = redoStack.popLast() else { return }; completedStrokes.append(stroke) }
    mutating func clear() { guard !completedStrokes.isEmpty else { return }; redoStack.append(contentsOf: completedStrokes.reversed()); completedStrokes.removeAll() }
}

enum DrawingRendererError: LocalizedError { case empty, cancelled, encoding }

struct DrawingRenderer: Sendable {
    static let logicalSize = CGSize(width: 1024, height: 1024)

    func render(_ state: DrawingCanvasState) async throws -> Data {
        guard !state.isEmpty else { throw DrawingRendererError.empty }
        return try await Task.detached(priority: .userInitiated) {
            guard !Task.isCancelled else { throw DrawingRendererError.cancelled }
            let format = UIGraphicsImageRendererFormat(); format.scale = 1; format.opaque = true
            let image = UIGraphicsImageRenderer(size: Self.logicalSize, format: format).image { context in
                UIColor.white.setFill(); context.fill(CGRect(origin: .zero, size: Self.logicalSize))
                for stroke in state.completedStrokes {
                    let points = stroke.points
                    guard let first = points.first else { continue }
                    let path = UIBezierPath(); path.move(to: CGPoint(x: first.x * Self.logicalSize.width, y: first.y * Self.logicalSize.height))
                    for point in points.dropFirst() { path.addLine(to: CGPoint(x: point.x * Self.logicalSize.width, y: point.y * Self.logicalSize.height)) }
                    if points.count == 1 { path.addArc(withCenter: CGPoint(x: first.x * Self.logicalSize.width, y: first.y * Self.logicalSize.height), radius: stroke.lineWidth / 2, startAngle: 0, endAngle: .pi * 2, clockwise: true) }
                    path.lineCapStyle = .round; path.lineJoinStyle = .round
                    path.lineWidth = stroke.lineWidth
                    (stroke.tool == .eraser ? UIColor.white : stroke.color.uiColor).setStroke()
                    path.stroke()
                }
            }
            guard !Task.isCancelled, let png = image.pngData() else { throw DrawingRendererError.encoding }
            return png
        }.value
    }
}

struct DrawingAnswerDraft: Codable, Equatable, Sendable {
    let roomCode: String
    let playerId: UUID
    let questionInstanceId: UUID
    var canvas: DrawingCanvasState
    var clientSubmissionId: UUID?
    var pngURL: URL?
    var previewPNG: Data?
}

enum DrawingAnswerDraftStorage {
    private static var directory: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("PartyGameDrawingDrafts", isDirectory: true) }
    private static func stem(_ draft: DrawingAnswerDraft) -> String { "\(draft.roomCode)-\(draft.playerId.uuidString)-\(draft.questionInstanceId.uuidString)" }

    static func load(roomCode: String, playerId: UUID, questionInstanceId: UUID) -> DrawingAnswerDraft? {
        let url = directory.appendingPathComponent("\(roomCode)-\(playerId.uuidString)-\(questionInstanceId.uuidString).json")
        return try? JSONDecoder().decode(DrawingAnswerDraft.self, from: Data(contentsOf: url))
    }
    static func save(_ draft: DrawingAnswerDraft) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try JSONEncoder().encode(draft).write(to: directory.appendingPathComponent("\(stem(draft)).json"), options: .atomic)
    }
    static func savePNG(_ data: Data, for draft: DrawingAnswerDraft) throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let url = directory.appendingPathComponent("\(stem(draft)).png")
        try data.write(to: url, options: .atomic); return url
    }
    static func remove(_ draft: DrawingAnswerDraft) {
        let json = directory.appendingPathComponent("\(stem(draft)).json")
        let png = draft.pngURL ?? directory.appendingPathComponent("\(stem(draft)).png")
        try? FileManager.default.removeItem(at: json); try? FileManager.default.removeItem(at: png)
    }
}
