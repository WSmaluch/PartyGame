import Foundation

struct FinalRoundPrivateState: Codable, Equatable, Sendable {
    let hasSubmittedSelfie: Bool
    let assignedArtifactId: UUID?
    let sourceDisplayMediaUrl: String?
    let sourceThumbnailMediaUrl: String?
    let hasSubmittedEdit: Bool
    let hasSubmittedVote: Bool
    /// The player-specific Final Selfie task is private server authority.  The
    /// public artifacts are presentation data and must not be used as a fallback.
    let selfiePrompt: LocalizedText?
    let targetRole: LocalizedText?
    let canSubmitSelfie: Bool?

    init(hasSubmittedSelfie: Bool, assignedArtifactId: UUID?, sourceDisplayMediaUrl: String?, sourceThumbnailMediaUrl: String?, hasSubmittedEdit: Bool, hasSubmittedVote: Bool, selfiePrompt: LocalizedText? = nil, targetRole: LocalizedText? = nil, canSubmitSelfie: Bool? = nil) {
        self.hasSubmittedSelfie = hasSubmittedSelfie
        self.assignedArtifactId = assignedArtifactId
        self.sourceDisplayMediaUrl = sourceDisplayMediaUrl
        self.sourceThumbnailMediaUrl = sourceThumbnailMediaUrl
        self.hasSubmittedEdit = hasSubmittedEdit
        self.hasSubmittedVote = hasSubmittedVote
        self.selfiePrompt = selfiePrompt
        self.targetRole = targetRole
        self.canSubmitSelfie = canSubmitSelfie
    }

    private enum CodingKeys: String, CodingKey {
        case hasSubmittedSelfie, assignedArtifactId, sourceDisplayMediaUrl, sourceThumbnailMediaUrl, hasSubmittedEdit, hasSubmittedVote, selfiePrompt, targetRole, canSubmitSelfie
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            hasSubmittedSelfie: try values.decode(Bool.self, forKey: .hasSubmittedSelfie),
            assignedArtifactId: try values.decodeIfPresent(UUID.self, forKey: .assignedArtifactId),
            sourceDisplayMediaUrl: try values.decodeIfPresent(String.self, forKey: .sourceDisplayMediaUrl),
            sourceThumbnailMediaUrl: try values.decodeIfPresent(String.self, forKey: .sourceThumbnailMediaUrl),
            hasSubmittedEdit: try values.decode(Bool.self, forKey: .hasSubmittedEdit),
            hasSubmittedVote: try values.decode(Bool.self, forKey: .hasSubmittedVote),
            selfiePrompt: try values.decodeIfPresent(LocalizedText.self, forKey: .selfiePrompt),
            targetRole: try values.decodeIfPresent(LocalizedText.self, forKey: .targetRole),
            canSubmitSelfie: try values.decodeIfPresent(Bool.self, forKey: .canSubmitSelfie))
    }
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
