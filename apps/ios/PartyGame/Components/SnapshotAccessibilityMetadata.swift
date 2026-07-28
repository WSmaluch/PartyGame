import Foundation

enum SnapshotAccessibilityMetadata {
    static func identifier(snapshot: RoomSnapshot, phase: String, questionId: UUID?) -> String {
        "game.snapshot|stateVersion=\(snapshot.stateVersion)|phase=\(phase)|questionId=\(questionId?.uuidString ?? "")"
    }
}
