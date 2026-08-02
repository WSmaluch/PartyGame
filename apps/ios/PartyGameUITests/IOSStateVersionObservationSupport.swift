import Foundation

enum IOSStateVersionObservationError: LocalizedError {
    case invalidIdentifier(String)
    case stateVersionRegression(previous: Int64, candidate: Int64)
    case missingDirectory(URL)
    case filenameCollision(URL)

    var errorDescription: String? {
        switch self {
        case .invalidIdentifier(let identifier):
            return "Niepoprawny identifier snapshotu: \(identifier)"
        case .stateVersionRegression(let previous, let candidate):
            return "Regresja stateVersion iOS: \(previous) -> \(candidate)"
        case .missingDirectory(let directory):
            return "Brak katalogu koordynacji iOS: \(directory.path)"
        case .filenameCollision(let target):
            return "Kolizja pliku obserwacji iOS: \(target.lastPathComponent)"
        }
    }
}

struct IOSStateVersionObservation: Codable, Equatable {
    let client: String
    let event: String
    let stateVersion: Int64
    let phase: String
    let questionId: String
    let gameStage: String
    let roomPhase: String
    let questionInstanceId: String
    let connectionState: String
    let timestampUtc: String

    init(
        event: String,
        stateVersion: Int64,
        phase: String,
        questionId: String,
        connectionState: String = "Unknown",
        timestampUtc: String = ISO8601DateFormatter().string(from: Date())
    ) {
        self.client = "ios"
        self.event = event
        self.stateVersion = stateVersion
        self.phase = phase
        self.questionId = questionId
        self.gameStage = phase
        self.roomPhase = phase == "Lobby" ? "Lobby" : (phase == "completed" ? "Completed" : "Started")
        self.questionInstanceId = questionId
        self.connectionState = connectionState
        self.timestampUtc = timestampUtc
    }

    static func parse(identifier: String, event: String, connectionState: String = "Unknown") throws -> Self {
        guard !event.isEmpty else { throw IOSStateVersionObservationError.invalidIdentifier(identifier) }
        let segments = identifier.split(separator: "|", omittingEmptySubsequences: false)
        guard segments.count == 4, segments[0] == "game.snapshot" else {
            throw IOSStateVersionObservationError.invalidIdentifier(identifier)
        }

        var fields = [String: String]()
        for segment in segments.dropFirst() {
            guard segment.filter({ $0 == "=" }).count == 1 else {
                throw IOSStateVersionObservationError.invalidIdentifier(identifier)
            }
            let pair = segment.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            guard pair.count == 2, !pair[0].isEmpty,
                  ["stateVersion", "phase", "questionId"].contains(String(pair[0])),
                  fields[String(pair[0])] == nil else {
                throw IOSStateVersionObservationError.invalidIdentifier(identifier)
            }
            fields[String(pair[0])] = String(pair[1])
        }

        guard fields.count == 3,
              let rawStateVersion = fields["stateVersion"],
              let stateVersion = Int64(rawStateVersion), stateVersion >= 0,
              let phase = fields["phase"], !phase.isEmpty,
              let questionId = fields["questionId"] else {
            throw IOSStateVersionObservationError.invalidIdentifier(identifier)
        }

        return Self(event: event, stateVersion: stateVersion, phase: phase, questionId: questionId, connectionState: connectionState)
    }
}

final class IOSStateVersionTracker {
    private(set) var observationCount = 0
    private(set) var regressionCount = 0
    private(set) var lastAcceptedStateVersion: Int64?

    func accept(_ candidate: Int64) throws {
        if let previous = lastAcceptedStateVersion, candidate < previous {
            regressionCount += 1
            throw IOSStateVersionObservationError.stateVersionRegression(previous: previous, candidate: candidate)
        }
        observationCount += 1
        lastAcceptedStateVersion = candidate
    }
}

final class IOSObservationWriter {
    private let directory: URL
    private var sequence = 0
    let tracker = IOSStateVersionTracker()

    init(directory: URL) throws {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: directory.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw IOSStateVersionObservationError.missingDirectory(directory)
        }
        self.directory = directory
    }

    func record(_ observation: IOSStateVersionObservation) throws {
        let nextSequence = sequence + 1
        let name = String(format: "ios-observation-%06d.json", nextSequence)
        let target = directory.appendingPathComponent(name)
        let temporary = directory.appendingPathComponent(".\(name).tmp")
        guard !FileManager.default.fileExists(atPath: target.path), !FileManager.default.fileExists(atPath: temporary.path) else {
            throw IOSStateVersionObservationError.filenameCollision(target)
        }

        try tracker.accept(observation.stateVersion)
        let data = try JSONEncoder().encode(observation)
        try data.write(to: temporary)
        defer { try? FileManager.default.removeItem(at: temporary) }
        try FileManager.default.moveItem(at: temporary, to: target)
        _ = try JSONDecoder().decode(IOSStateVersionObservation.self, from: Data(contentsOf: target))
        sequence = nextSequence
    }

    func writeMarkerOnce(_ name: String, observation: IOSStateVersionObservation, rankingCount: Int? = nil) throws {
        let target = directory.appendingPathComponent(name)
        if FileManager.default.fileExists(atPath: target.path) {
            _ = try JSONDecoder().decode(IOSDiagnosticMarker.self, from: Data(contentsOf: target))
            return
        }

        let marker = IOSDiagnosticMarker(observation: observation, rankingCount: rankingCount)
        let temporary = directory.appendingPathComponent(".\(name).tmp")
        let data = try JSONEncoder().encode(marker)
        try data.write(to: temporary)
        defer { try? FileManager.default.removeItem(at: temporary) }
        try FileManager.default.moveItem(at: temporary, to: target)
        _ = try JSONDecoder().decode(IOSDiagnosticMarker.self, from: Data(contentsOf: target))
    }
}

private struct IOSDiagnosticMarker: Codable {
    let event: String
    let stateVersion: Int64
    let gameStage: String
    let roomPhase: String
    let questionInstanceId: String
    let connectionState: String
    let timestampUtc: String
    let rankingCount: Int?

    init(observation: IOSStateVersionObservation, rankingCount: Int?) {
        event = observation.event
        stateVersion = observation.stateVersion
        gameStage = observation.gameStage
        roomPhase = observation.roomPhase
        questionInstanceId = observation.questionInstanceId
        connectionState = observation.connectionState
        timestampUtc = observation.timestampUtc
        self.rankingCount = rankingCount
    }
}
