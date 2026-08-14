import Foundation

struct FinalRoundPrivateState: Codable, Equatable, Sendable {
    let hasSubmittedSelfie: Bool
    let assignedArtifactId: UUID?
    let sourceDisplayMediaUrl: String?
    let sourceThumbnailMediaUrl: String?
    let hasSubmittedEdit: Bool
    let hasSubmittedVote: Bool
}

struct FinalRoundArtifact: Codable, Equatable, Identifiable, Sendable {
    let artifactId: UUID
    let subjectPlayerId: UUID
    let subjectNickname: String
    let selfiePrompt: LocalizedText
    let targetRole: LocalizedText
    let displayMediaUrl: String?
    let thumbnailMediaUrl: String?
    let voteCount: Int
    let isTopResult: Bool
    var id: UUID { artifactId }
}

struct FinalRoundSnapshot: Codable, Equatable, Sendable {
    let currentPass: Int
    let totalPasses: Int
    let submittedSelfies: Int
    let requiredSelfies: Int
    let submittedEdits: Int
    let requiredEdits: Int
    let submittedVotes: Int
    let requiredVotes: Int
    let artifacts: [FinalRoundArtifact]
    let editAssignments: [FinalRoundEditAssignment]?
}

struct FinalRoundEditAssignment: Codable, Equatable, Identifiable, Sendable {
    let artifactId: UUID
    let editorPlayerId: UUID
    let sourceDisplayMediaUrl: String
    let sourceThumbnailMediaUrl: String
    var id: UUID { artifactId }
}
