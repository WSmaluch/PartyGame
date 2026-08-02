import Foundation

enum RoomPhase: String, Codable, Equatable, Sendable {
    case lobby = "Lobby"
    case started = "Started"
    case completed = "Completed"
}

enum QuestionType: Codable, Equatable, Sendable {
    case playerSelection, textAnswer, photoAnswer, drawingAnswer, unknown(String)

    init(from decoder: Decoder) throws {
        let value = try decoder.singleValueContainer().decode(String.self)
        switch value {
        case "PlayerSelection": self = .playerSelection
        case "TextAnswer": self = .textAnswer
        case "PhotoAnswer": self = .photoAnswer
        case "DrawingAnswer": self = .drawingAnswer
        default: self = .unknown(value)
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .playerSelection: try container.encode("PlayerSelection")
        case .textAnswer: try container.encode("TextAnswer")
        case .photoAnswer: try container.encode("PhotoAnswer")
        case .drawingAnswer: try container.encode("DrawingAnswer")
        case .unknown(let value): try container.encode(value)
        }
    }
}

struct RoomSettings: Codable, Equatable, Sendable {
    var roundCount = 4
    var questionsPerRound = 5
    var playerSelectionSeconds = 20
    var textAnswerSeconds = 40
    var votingSeconds = 20
    var photoSeconds = 45
    var drawingSeconds = 90
    var resultPresentationSeconds = 8
    var finalRoundEnabled = true
    var finalDrawingPasses = 3

    var isValid: Bool {
        (1 ... 10).contains(roundCount)
            && (4 ... 6).contains(questionsPerRound)
            && (5 ... 120).contains(playerSelectionSeconds)
            && (5 ... 180).contains(textAnswerSeconds)
            && (5 ... 120).contains(votingSeconds)
            && (10 ... 180).contains(photoSeconds)
            && (30 ... 300).contains(drawingSeconds)
            && (3 ... 30).contains(resultPresentationSeconds)
            && (1 ... 9).contains(finalDrawingPasses)
    }
}

struct RoomPlayer: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let nickname: String
    let isHost: Bool
    let isReady: Bool
    let isConnected: Bool
    let hasProfilePhoto: Bool
    let profilePhotoUrl: String?
    let score: Int
}

struct PlayerScoreSnapshot: Codable, Equatable, Sendable {
    let playerId: UUID
    let score: Int
}

enum GameStage: Codable, Equatable, Sendable {
    case categoryIntro
    case questionIntro
    case collectingPlayerSelections
    case showingQuestionResults
    case roundSummary
    case pausedForDisplay
    case completed
    case collectingTextAnswers
    case revealingTextAnswers
    case collectingTextAnswerVotes
    case showingTextAnswerResults
    case collectingPhotoAnswers
    case revealingPhotoAnswers
    case collectingPhotoAnswerVotes
    case showingPhotoAnswerResults
    case collectingDrawingAnswers
    case revealingDrawingAnswers
    case collectingDrawingAnswerVotes
    case showingDrawingAnswerResults
    case unknown(String)

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        let value = try container.decode(String.self)
        switch value {
        case "CategoryIntro": self = .categoryIntro
        case "QuestionIntro": self = .questionIntro
        case "CollectingPlayerSelections": self = .collectingPlayerSelections
        case "ShowingQuestionResults": self = .showingQuestionResults
        case "RoundSummary": self = .roundSummary
        case "PausedForDisplay": self = .pausedForDisplay
        case "Completed": self = .completed
        case "CollectingTextAnswers": self = .collectingTextAnswers
        case "RevealingTextAnswers": self = .revealingTextAnswers
        case "CollectingTextAnswerVotes": self = .collectingTextAnswerVotes
        case "ShowingTextAnswerResults": self = .showingTextAnswerResults
        case "CollectingPhotoAnswers": self = .collectingPhotoAnswers
        case "RevealingPhotoAnswers": self = .revealingPhotoAnswers
        case "CollectingPhotoAnswerVotes": self = .collectingPhotoAnswerVotes
        case "ShowingPhotoAnswerResults": self = .showingPhotoAnswerResults
        case "CollectingDrawingAnswers": self = .collectingDrawingAnswers
        case "RevealingDrawingAnswers": self = .revealingDrawingAnswers
        case "CollectingDrawingAnswerVotes": self = .collectingDrawingAnswerVotes
        case "ShowingDrawingAnswerResults": self = .showingDrawingAnswerResults
        default: self = .unknown(value)
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .categoryIntro: try container.encode("CategoryIntro")
        case .questionIntro: try container.encode("QuestionIntro")
        case .collectingPlayerSelections: try container.encode("CollectingPlayerSelections")
        case .showingQuestionResults: try container.encode("ShowingQuestionResults")
        case .roundSummary: try container.encode("RoundSummary")
        case .pausedForDisplay: try container.encode("PausedForDisplay")
        case .completed: try container.encode("Completed")
        case .collectingTextAnswers: try container.encode("CollectingTextAnswers")
        case .revealingTextAnswers: try container.encode("RevealingTextAnswers")
        case .collectingTextAnswerVotes: try container.encode("CollectingTextAnswerVotes")
        case .showingTextAnswerResults: try container.encode("ShowingTextAnswerResults")
        case .collectingPhotoAnswers: try container.encode("CollectingPhotoAnswers")
        case .revealingPhotoAnswers: try container.encode("RevealingPhotoAnswers")
        case .collectingPhotoAnswerVotes: try container.encode("CollectingPhotoAnswerVotes")
        case .showingPhotoAnswerResults: try container.encode("ShowingPhotoAnswerResults")
        case .collectingDrawingAnswers: try container.encode("CollectingDrawingAnswers")
        case .revealingDrawingAnswers: try container.encode("RevealingDrawingAnswers")
        case .collectingDrawingAnswerVotes: try container.encode("CollectingDrawingAnswerVotes")
        case .showingDrawingAnswerResults: try container.encode("ShowingDrawingAnswerResults")
        case .unknown(let val): try container.encode(val)
        }
    }
}

struct LocalizedText: Codable, Equatable, Sendable {
    let defaultText: String
    let translations: [String: String]?
    
    var local: String { translations?[Locale.current.language.languageCode?.identifier ?? "en"] ?? defaultText }

    init(defaultText: String, translations: [String: String]?) {
        self.defaultText = defaultText; self.translations = translations
    }

    private enum CodingKeys: String, CodingKey { case defaultText, translations, pl, en }
    init(from decoder: Decoder) throws {
        let scalarValues = try decoder.singleValueContainer()
        if let localized = try? scalarValues.decode([String: String].self) {
            let en = localized["en"]
            let pl = localized["pl"]
            defaultText = localized["defaultText"] ?? en ?? pl ?? ""
            translations = ["en": en, "pl": pl].compactMapValues { $0 }
            return
        }
        if let scalar = try? scalarValues.decode(String.self) {
            defaultText = scalar; translations = nil; return
        }
        let values = try decoder.container(keyedBy: CodingKeys.self)
        let en = try values.decodeIfPresent(String.self, forKey: .en)
        let pl = try values.decodeIfPresent(String.self, forKey: .pl)
        defaultText = try values.decodeIfPresent(String.self, forKey: .defaultText) ?? en ?? pl ?? ""
        translations = try values.decodeIfPresent([String: String].self, forKey: .translations)
            ?? ["en": en, "pl": pl].compactMapValues { $0 }
    }
    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(defaultText, forKey: .defaultText)
        try values.encodeIfPresent(translations, forKey: .translations)
    }
}

struct GameCategorySnapshot: Codable, Equatable, Sendable {
    let id: UUID
    let name: String
    let backgroundHexColor: String

    init(id: UUID, name: String, backgroundHexColor: String) {
        self.id = id; self.name = name; self.backgroundHexColor = backgroundHexColor
    }
    private enum CodingKeys: String, CodingKey { case id, name, backgroundHexColor }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decode(UUID.self, forKey: .id)
        if let text = try? values.decode(LocalizedText.self, forKey: .name) { name = text.local }
        else { name = try values.decodeIfPresent(String.self, forKey: .name) ?? "" }
        backgroundHexColor = try values.decodeIfPresent(String.self, forKey: .backgroundHexColor) ?? "#241146"
    }
}

struct GameQuestionSnapshot: Codable, Equatable, Sendable {
    let instanceId: UUID
    let categoryId: UUID
    let questionText: LocalizedText
    let requiredAnswerType: String

    init(instanceId: UUID, categoryId: UUID, questionText: LocalizedText, requiredAnswerType: String) {
        self.instanceId = instanceId; self.categoryId = categoryId; self.questionText = questionText; self.requiredAnswerType = requiredAnswerType
    }
    private enum CodingKeys: String, CodingKey { case instanceId, id, categoryId, questionText, text, requiredAnswerType }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        instanceId = try values.decodeIfPresent(UUID.self, forKey: .instanceId) ?? values.decode(UUID.self, forKey: .id)
        categoryId = try values.decodeIfPresent(UUID.self, forKey: .categoryId) ?? UUID()
        questionText = try values.decodeIfPresent(LocalizedText.self, forKey: .questionText)
            ?? values.decode(LocalizedText.self, forKey: .text)
        requiredAnswerType = try values.decodeIfPresent(String.self, forKey: .requiredAnswerType) ?? ""
    }
    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(instanceId, forKey: .instanceId)
        try values.encode(categoryId, forKey: .categoryId)
        try values.encode(questionText, forKey: .questionText)
        try values.encode(requiredAnswerType, forKey: .requiredAnswerType)
    }
}

struct ResultVoter: Codable, Equatable, Sendable {
    let playerId: UUID
    let nickname: String?
    let profilePhotoUrl: String?
    let pointsAwarded: Int
}

struct PlayerSelectionResultOption: Codable, Equatable, Sendable {
    let selectedPlayerId: UUID
    let selectedPlayerNickname: String
    let selectedPlayerPhotoUrl: String?
    let voteCount: Int
    let isTopResult: Bool
    let voters: [ResultVoter]
}

struct PlayerSelectionResultRow: Equatable, Sendable {
    let playerId: UUID
    let selectedPlayerId: UUID
    let pointsAwarded: Int
}

struct PlayerSelectionResults: Codable, Equatable, Sendable {
    let questionInstanceId: UUID
    let answeredPlayers: Int
    let requiredPlayers: Int
    let missingPlayers: Int
    let highestVoteCount: Int
    let options: [PlayerSelectionResultOption]

    var results: [PlayerSelectionResultRow] {
        options.flatMap { option in
            option.voters.map { PlayerSelectionResultRow(playerId: $0.playerId,
                selectedPlayerId: option.selectedPlayerId, pointsAwarded: $0.pointsAwarded) }
        }
    }
}

struct RankingEntry: Codable, Equatable, Sendable {
    let playerId: UUID
    let nickname: String?
    let profilePhotoUrl: String?
    let score: Int
    let previousScore: Int
    let position: Int
    let previousPosition: Int

    private enum CodingKeys: String, CodingKey {
        case playerId, nickname, profilePhotoUrl, score, previousScore, position, previousPosition, rank
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        playerId = try values.decode(UUID.self, forKey: .playerId)
        nickname = try values.decodeIfPresent(String.self, forKey: .nickname)
        profilePhotoUrl = try values.decodeIfPresent(String.self, forKey: .profilePhotoUrl)
        score = try values.decodeIfPresent(Int.self, forKey: .score) ?? 0
        previousScore = try values.decodeIfPresent(Int.self, forKey: .previousScore) ?? score
        position = try values.decodeIfPresent(Int.self, forKey: .rank)
            ?? values.decodeIfPresent(Int.self, forKey: .position) ?? 0
        previousPosition = try values.decodeIfPresent(Int.self, forKey: .previousPosition) ?? position
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(playerId, forKey: .playerId)
        try values.encodeIfPresent(nickname, forKey: .nickname)
        try values.encodeIfPresent(profilePhotoUrl, forKey: .profilePhotoUrl)
        try values.encode(score, forKey: .score)
        try values.encode(position, forKey: .rank)
    }
}

struct RoundSummarySnapshot: Codable, Equatable, Sendable {
    let roundNumber: Int
    let rankings: [RankingEntry]

    private enum CodingKeys: String, CodingKey { case roundNumber, ranking, rankings }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        roundNumber = try values.decodeIfPresent(Int.self, forKey: .roundNumber) ?? 0
        rankings = try values.decodeIfPresent([RankingEntry].self, forKey: .ranking)
            ?? values.decodeIfPresent([RankingEntry].self, forKey: .rankings) ?? []
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(roundNumber, forKey: .roundNumber)
        try values.encode(rankings, forKey: .ranking)
    }
}

struct TextAnswerOptionVoting: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { answerId }
    let answerId: UUID
    let text: String
    let displayOrder: Int?
}

struct TextAnswerOptionResult: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { answerId }
    let answerId: UUID
    let text: String
    let authorPlayerId: UUID
    let authorPlayerNickname: String
    let authorPlayerPhotoUrl: String?
    let voteCount: Int
    let isTopResult: Bool
    let voters: [ResultVoter]
}

struct TextAnswerResults: Codable, Equatable, Sendable {
    let questionInstanceId: UUID
    let answeredPlayers: Int
    let requiredPlayers: Int
    let missingPlayers: Int?
    let highestVoteCount: Int?
    let options: [TextAnswerOptionResult]?
    let votingOptions: [TextAnswerOptionVoting]?
    let submittedAnswerPlayerIds: [UUID]?
}

struct AnonymousPhotoAnswer: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { photoAnswerId }
    let photoAnswerId: UUID
    let displayPhotoUrl: String?
    let thumbnailPhotoUrl: String?
    let displayOrder: Int
    let width: Int
    let height: Int
}

struct PhotoAnswerResultVoter: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { playerId }
    let playerId: UUID
    let nickname: String
    let profilePhotoUrl: String?
    let pointsAwarded: Int
}

struct PhotoAnswerResultOption: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { photoAnswerId }
    let photoAnswerId: UUID
    let displayPhotoUrl: String?
    let thumbnailPhotoUrl: String?
    let width: Int
    let height: Int
    let authorPlayerId: UUID
    let authorNickname: String
    let authorPhotoUrl: String?
    let voteCount: Int
    let isTopResult: Bool
    let voters: [PhotoAnswerResultVoter]
}

struct PhotoAnswerResults: Codable, Equatable, Sendable {
    let questionInstanceId: UUID?
    let submittedPlayers: Int
    let requiredPlayers: Int
    let votedPlayers: Int?
    let requiredVoters: Int?
    let missingSubmissionPlayers: Int?
    let missingVotePlayers: Int?
    let highestVoteCount: Int?
    let options: [PhotoAnswerResultOption]?
    let anonymousOptions: [AnonymousPhotoAnswer]?
}

struct AnonymousDrawingOption: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { drawingAnswerId }
    let drawingAnswerId: UUID
    let displayDrawingUrl: String?
    let thumbnailDrawingUrl: String?
    let displayOrder: Int?
    let revealOrder: Int?
    let width: Int
    let height: Int
}

struct DrawingAnswerResultVoter: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { playerId }
    let playerId: UUID
    let nickname: String
    let profilePhotoUrl: String?
    let pointsAwarded: Int
}

struct DrawingAnswerResultOption: Codable, Equatable, Sendable, Identifiable {
    var id: UUID { drawingAnswerId }
    let drawingAnswerId: UUID
    let displayDrawingUrl: String?
    let thumbnailDrawingUrl: String?
    let width: Int
    let height: Int
    let authorPlayerId: UUID
    let authorNickname: String
    let authorPhotoUrl: String?
    let voteCount: Int
    let isTopResult: Bool
    let voters: [DrawingAnswerResultVoter]
}

struct DrawingAnswerResultsSnapshot: Codable, Equatable, Sendable {
    let questionInstanceId: UUID?
    let submittedDrawingAnswers: Int?
    let requiredDrawingAnswers: Int?
    let submittedDrawingAnswerPlayerIds: [UUID]?
    let votedPlayers: Int?
    let requiredVoters: Int?
    let highestVoteCount: Int?
    let options: [DrawingAnswerResultOption]?
    let anonymousOptions: [AnonymousDrawingOption]?
}

extension DrawingAnswerResultsSnapshot {
    private enum CodingKeys: String, CodingKey {
        case questionInstanceId, submittedPlayers, requiredPlayers
        case submittedDrawingAnswers, requiredDrawingAnswers
        case submittedDrawingAnswerPlayerIds, votedPlayers, requiredVoters, highestVoteCount, options, anonymousOptions
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        questionInstanceId = try values.decodeIfPresent(UUID.self, forKey: .questionInstanceId)
        submittedDrawingAnswers = try values.decodeIfPresent(Int.self, forKey: .submittedPlayers)
            ?? values.decodeIfPresent(Int.self, forKey: .submittedDrawingAnswers)
        requiredDrawingAnswers = try values.decodeIfPresent(Int.self, forKey: .requiredPlayers)
            ?? values.decodeIfPresent(Int.self, forKey: .requiredDrawingAnswers)
        submittedDrawingAnswerPlayerIds = try values.decodeIfPresent([UUID].self, forKey: .submittedDrawingAnswerPlayerIds)
        votedPlayers = try values.decodeIfPresent(Int.self, forKey: .votedPlayers)
        requiredVoters = try values.decodeIfPresent(Int.self, forKey: .requiredVoters)
        highestVoteCount = try values.decodeIfPresent(Int.self, forKey: .highestVoteCount)
        options = try values.decodeIfPresent([DrawingAnswerResultOption].self, forKey: .options)
        anonymousOptions = try values.decodeIfPresent([AnonymousDrawingOption].self, forKey: .anonymousOptions)
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encodeIfPresent(questionInstanceId, forKey: .questionInstanceId)
        try values.encodeIfPresent(submittedDrawingAnswers, forKey: .submittedPlayers)
        try values.encodeIfPresent(requiredDrawingAnswers, forKey: .requiredPlayers)
        try values.encodeIfPresent(submittedDrawingAnswerPlayerIds, forKey: .submittedDrawingAnswerPlayerIds)
        try values.encodeIfPresent(votedPlayers, forKey: .votedPlayers)
        try values.encodeIfPresent(requiredVoters, forKey: .requiredVoters)
        try values.encodeIfPresent(highestVoteCount, forKey: .highestVoteCount)
        try values.encodeIfPresent(options, forKey: .options)
        try values.encodeIfPresent(anonymousOptions, forKey: .anonymousOptions)
    }
}

struct PlayerPrivateGameState: Codable, Equatable, Sendable {
    let playerId: UUID
    let questionInstanceId: UUID?
    let hasSubmittedTextAnswer: Bool
    let ownTextAnswerId: UUID?
    let hasSubmittedTextAnswerVote: Bool
    let isEligibleForTextAnswerVote: Bool
    let hasSubmittedPhotoAnswer: Bool
    let ownPhotoAnswerId: UUID?
    let hasSubmittedPhotoAnswerVote: Bool
    let hasSubmittedDrawingAnswer: Bool
    let ownDrawingAnswerId: UUID?
    let hasSubmittedDrawingAnswerVote: Bool
    let isEligibleForDrawingAnswer: Bool

    init(
        playerId: UUID,
        questionInstanceId: UUID?,
        hasSubmittedTextAnswer: Bool,
        ownTextAnswerId: UUID?,
        hasSubmittedTextAnswerVote: Bool,
        isEligibleForTextAnswerVote: Bool = false,
        hasSubmittedPhotoAnswer: Bool = false,
        ownPhotoAnswerId: UUID? = nil,
        hasSubmittedPhotoAnswerVote: Bool = false,
        hasSubmittedDrawingAnswer: Bool = false,
        ownDrawingAnswerId: UUID? = nil,
        hasSubmittedDrawingAnswerVote: Bool = false,
        isEligibleForDrawingAnswer: Bool = false
    ) {
        self.playerId = playerId
        self.questionInstanceId = questionInstanceId
        self.hasSubmittedTextAnswer = hasSubmittedTextAnswer
        self.ownTextAnswerId = ownTextAnswerId
        self.hasSubmittedTextAnswerVote = hasSubmittedTextAnswerVote
        self.isEligibleForTextAnswerVote = isEligibleForTextAnswerVote
        self.hasSubmittedPhotoAnswer = hasSubmittedPhotoAnswer
        self.ownPhotoAnswerId = ownPhotoAnswerId
        self.hasSubmittedPhotoAnswerVote = hasSubmittedPhotoAnswerVote
        self.hasSubmittedDrawingAnswer = hasSubmittedDrawingAnswer
        self.ownDrawingAnswerId = ownDrawingAnswerId
        self.hasSubmittedDrawingAnswerVote = hasSubmittedDrawingAnswerVote
        self.isEligibleForDrawingAnswer = isEligibleForDrawingAnswer
    }

    private enum CodingKeys: String, CodingKey {
        case playerId, questionInstanceId, hasSubmittedTextAnswer, ownTextAnswerId, hasSubmittedTextAnswerVote, isEligibleForTextAnswerVote
        case hasSubmittedPhotoAnswer, ownPhotoAnswerId, hasSubmittedPhotoAnswerVote
        case hasSubmittedDrawingAnswer, ownDrawingAnswerId, hasSubmittedDrawingAnswerVote, isEligibleForDrawingAnswer
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        playerId = try values.decode(UUID.self, forKey: .playerId)
        questionInstanceId = try values.decodeIfPresent(UUID.self, forKey: .questionInstanceId)
        hasSubmittedTextAnswer = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedTextAnswer) ?? false
        ownTextAnswerId = try values.decodeIfPresent(UUID.self, forKey: .ownTextAnswerId)
        hasSubmittedTextAnswerVote = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedTextAnswerVote) ?? false
        isEligibleForTextAnswerVote = try values.decodeIfPresent(Bool.self, forKey: .isEligibleForTextAnswerVote) ?? false
        hasSubmittedPhotoAnswer = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedPhotoAnswer) ?? false
        ownPhotoAnswerId = try values.decodeIfPresent(UUID.self, forKey: .ownPhotoAnswerId)
        hasSubmittedPhotoAnswerVote = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedPhotoAnswerVote) ?? false
        hasSubmittedDrawingAnswer = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedDrawingAnswer) ?? false
        ownDrawingAnswerId = try values.decodeIfPresent(UUID.self, forKey: .ownDrawingAnswerId)
        hasSubmittedDrawingAnswerVote = try values.decodeIfPresent(Bool.self, forKey: .hasSubmittedDrawingAnswerVote) ?? false
        isEligibleForDrawingAnswer = try values.decodeIfPresent(Bool.self, forKey: .isEligibleForDrawingAnswer) ?? false
    }
}

struct GameSnapshot: Codable, Equatable, Sendable {
    let stage: GameStage
    let currentRoundNumber: Int
    let totalRounds: Int
    let currentQuestionNumber: Int
    let questionsInCurrentRound: Int
    let stageEndsAtUtc: String?
    let pausedAtUtc: String?
    let pausedStage: GameStage?
    let pausedRemainingMilliseconds: Double?
    let scores: [PlayerScoreSnapshot]
    let categories: [GameCategorySnapshot]?
    let currentQuestion: GameQuestionSnapshot?
    let playerSelectionResults: PlayerSelectionResults?
    let roundSummary: RoundSummarySnapshot?
    let ranking: [RankingEntry]?
    let textAnswerResults: TextAnswerResults?
    let photoAnswerResults: PhotoAnswerResults?
    let drawingAnswerResults: DrawingAnswerResultsSnapshot?

    init(
        stage: GameStage, currentRoundNumber: Int, totalRounds: Int, currentQuestionNumber: Int,
        questionsInCurrentRound: Int, stageEndsAtUtc: String?, pausedAtUtc: String?, pausedStage: GameStage?,
        pausedRemainingMilliseconds: Double?, scores: [PlayerScoreSnapshot], categories: [GameCategorySnapshot]?,
        currentQuestion: GameQuestionSnapshot?, playerSelectionResults: PlayerSelectionResults?,
        roundSummary: RoundSummarySnapshot?, textAnswerResults: TextAnswerResults?, photoAnswerResults: PhotoAnswerResults? = nil,
        ranking: [RankingEntry]? = nil,
        drawingAnswerResults: DrawingAnswerResultsSnapshot? = nil
    ) {
        self.stage = stage; self.currentRoundNumber = currentRoundNumber; self.totalRounds = totalRounds
        self.currentQuestionNumber = currentQuestionNumber; self.questionsInCurrentRound = questionsInCurrentRound
        self.stageEndsAtUtc = stageEndsAtUtc; self.pausedAtUtc = pausedAtUtc; self.pausedStage = pausedStage
        self.pausedRemainingMilliseconds = pausedRemainingMilliseconds; self.scores = scores; self.categories = categories
        self.currentQuestion = currentQuestion; self.playerSelectionResults = playerSelectionResults
        self.roundSummary = roundSummary; self.ranking = ranking; self.textAnswerResults = textAnswerResults; self.photoAnswerResults = photoAnswerResults
        self.drawingAnswerResults = drawingAnswerResults
    }

    private enum CodingKeys: String, CodingKey {
        case stage, currentRoundNumber, totalRounds, currentQuestionNumber, questionsInCurrentRound, stageEndsAtUtc
        case pausedAtUtc, pausedStage, pausedRemainingMilliseconds, scores, categories, category, currentQuestion, question
        case playerSelectionResults, results, roundSummary, ranking, textAnswerResults, textResults, photoAnswerResults, drawingAnswerResults
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        stage = try values.decode(GameStage.self, forKey: .stage)
        currentRoundNumber = try values.decodeIfPresent(Int.self, forKey: .currentRoundNumber) ?? 0
        totalRounds = try values.decodeIfPresent(Int.self, forKey: .totalRounds) ?? 0
        currentQuestionNumber = try values.decodeIfPresent(Int.self, forKey: .currentQuestionNumber) ?? 0
        questionsInCurrentRound = try values.decodeIfPresent(Int.self, forKey: .questionsInCurrentRound) ?? 0
        stageEndsAtUtc = try values.decodeIfPresent(String.self, forKey: .stageEndsAtUtc)
        pausedAtUtc = try values.decodeIfPresent(String.self, forKey: .pausedAtUtc)
        pausedStage = try values.decodeIfPresent(GameStage.self, forKey: .pausedStage)
        pausedRemainingMilliseconds = try values.decodeIfPresent(Double.self, forKey: .pausedRemainingMilliseconds)
        scores = try values.decodeIfPresent([PlayerScoreSnapshot].self, forKey: .scores) ?? []
        categories = try values.decodeIfPresent([GameCategorySnapshot].self, forKey: .categories)
            ?? values.decodeIfPresent(GameCategorySnapshot.self, forKey: .category).map { [$0] }
        currentQuestion = try values.decodeIfPresent(GameQuestionSnapshot.self, forKey: .currentQuestion)
            ?? values.decodeIfPresent(GameQuestionSnapshot.self, forKey: .question)
        playerSelectionResults = try values.decodeIfPresent(PlayerSelectionResults.self, forKey: .playerSelectionResults)
            ?? values.decodeIfPresent(PlayerSelectionResults.self, forKey: .results)
        roundSummary = try values.decodeIfPresent(RoundSummarySnapshot.self, forKey: .roundSummary)
        ranking = try values.decodeIfPresent([RankingEntry].self, forKey: .ranking)
        textAnswerResults = try values.decodeIfPresent(TextAnswerResults.self, forKey: .textAnswerResults)
            ?? values.decodeIfPresent(TextAnswerResults.self, forKey: .textResults)
        photoAnswerResults = try values.decodeIfPresent(PhotoAnswerResults.self, forKey: .photoAnswerResults)
        drawingAnswerResults = try values.decodeIfPresent(DrawingAnswerResultsSnapshot.self, forKey: .drawingAnswerResults)
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(stage, forKey: .stage)
        try values.encode(currentRoundNumber, forKey: .currentRoundNumber)
        try values.encode(totalRounds, forKey: .totalRounds)
        try values.encode(currentQuestionNumber, forKey: .currentQuestionNumber)
        try values.encode(questionsInCurrentRound, forKey: .questionsInCurrentRound)
        try values.encodeIfPresent(stageEndsAtUtc, forKey: .stageEndsAtUtc)
        try values.encodeIfPresent(pausedAtUtc, forKey: .pausedAtUtc)
        try values.encodeIfPresent(pausedStage, forKey: .pausedStage)
        try values.encodeIfPresent(pausedRemainingMilliseconds, forKey: .pausedRemainingMilliseconds)
        try values.encode(scores, forKey: .scores)
        try values.encodeIfPresent(categories, forKey: .categories)
        try values.encodeIfPresent(currentQuestion, forKey: .currentQuestion)
        try values.encodeIfPresent(playerSelectionResults, forKey: .playerSelectionResults)
        try values.encodeIfPresent(roundSummary, forKey: .roundSummary)
        try values.encodeIfPresent(ranking, forKey: .ranking)
        try values.encodeIfPresent(textAnswerResults, forKey: .textAnswerResults)
        try values.encodeIfPresent(photoAnswerResults, forKey: .photoAnswerResults)
        try values.encodeIfPresent(drawingAnswerResults, forKey: .drawingAnswerResults)
    }
}

extension GameSnapshot {
    /// Transport actions use the persisted game-question instance id. Older
    /// servers exposed only the question-definition id as `question.id`, so
    /// media-result snapshots remain a backwards-compatible fallback.
    var resolvedQuestionInstanceId: UUID? {
        photoAnswerResults?.questionInstanceId ?? drawingAnswerResults?.questionInstanceId ?? currentQuestion?.instanceId
    }
}

struct RoomSnapshot: Codable, Equatable, Sendable {
    let roomCode: String
    let phase: RoomPhase
    let stateVersion: Int64
    let displayConnected: Bool
    let minimumPlayers: Int
    let maximumPlayers: Int
    let canStart: Bool
    let settings: RoomSettings
    let players: [RoomPlayer]
    let createdAtUtc: String
    let startedAtUtc: String?
    let game: GameSnapshot?
}

struct CreateRoomRequest: Codable, Equatable, Sendable {
    let nickname: String
    let settings: RoomSettings
    let selectedPackageKeys: [String]?
}

struct JoinRoomRequest: Codable, Equatable, Sendable {
    let nickname: String
}

struct RoomAccessResponse: Codable, Equatable, Sendable {
    let roomCode: String
    let playerId: UUID
    let reconnectToken: String
    let snapshot: RoomSnapshot
    let privateState: PlayerPrivateGameState
}

typealias CreateRoomResponse = RoomAccessResponse
typealias JoinRoomResponse = RoomAccessResponse

struct ResumePlayerResponse: Codable, Equatable, Sendable {
    let player: RoomPlayer
    let snapshot: RoomSnapshot
    let privateState: PlayerPrivateGameState
}

struct ProblemDetails: Codable, Equatable, Sendable {
    let type: String?
    let title: String?
    let status: Int?
    let detail: String?
    let instance: String?
    let errors: [String: [String]]?
    let code: String?

    init(type: String? = nil, title: String? = nil, status: Int? = nil, detail: String? = nil, instance: String? = nil, errors: [String: [String]]? = nil, code: String? = nil) {
        self.type = type; self.title = title; self.status = status; self.detail = detail; self.instance = instance; self.errors = errors; self.code = code
    }

    private enum CodingKeys: String, CodingKey { case type, title, status, detail, instance, errors, code }

    var userMessage: String {
        if let first = errors?.sorted(by: { $0.key < $1.key }).first?.value.first {
            return first
        }
        return detail ?? title ?? String(localized: "error.invalid_response")
    }
}

typealias ValidationProblemDetails = ProblemDetails

struct SnapshotAccumulator: Equatable, Sendable {
    private(set) var snapshot: RoomSnapshot?

    @discardableResult
    mutating func accept(_ candidate: RoomSnapshot) -> Bool {
        guard snapshot == nil || candidate.stateVersion > snapshot!.stateVersion else {
            return false
        }
        snapshot = candidate
        return true
    }
}

struct ContentPackage: Codable, Equatable, Sendable, Identifiable {
    var id: String { key }
    let key: String
    let name: String
}
