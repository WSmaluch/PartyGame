import XCTest

final class IOSStateVersionObservationSupportTests: XCTestCase {
    func testParserAcceptsValidIdentifier() throws {
        let observation = try IOSStateVersionObservation.parse(
            identifier: "game.snapshot|stateVersion=12|phase=Lobby|questionId=",
            event: "snapshot-lobby-accepted"
        )

        XCTAssertEqual(observation.client, "ios")
        XCTAssertEqual(observation.event, "snapshot-lobby-accepted")
        XCTAssertEqual(observation.stateVersion, 12)
        XCTAssertEqual(observation.phase, "Lobby")
        XCTAssertEqual(observation.questionId, "")
    }

    func testParserRejectsEachMalformedRequiredCase() {
        let invalidIdentifiers = [
            "snapshot|stateVersion=1|phase=Lobby|questionId=",
            "game.snapshot|phase=Lobby|questionId=",
            "game.snapshot|stateVersion=1|questionId=",
            "game.snapshot|stateVersion=1|phase=Lobby",
            "game.snapshot|stateVersion=abc|phase=Lobby|questionId=",
            "game.snapshot|stateVersion=-1|phase=Lobby|questionId=",
            "game.snapshot|stateVersion=1|stateVersion=2|phase=Lobby",
            "game.snapshot|stateVersion=1=2|phase=Lobby|questionId=",
        ]

        for identifier in invalidIdentifiers {
            XCTAssertThrowsError(try IOSStateVersionObservation.parse(identifier: identifier, event: "event"), "Expected parser failure for \(identifier)")
        }
    }

    func testTrackerAcceptsMonotonicVersionsIncludingDuplicates() throws {
        let tracker = IOSStateVersionTracker()

        try tracker.accept(10)
        try tracker.accept(11)
        try tracker.accept(11)
        try tracker.accept(12)

        XCTAssertEqual(tracker.observationCount, 4)
        XCTAssertEqual(tracker.regressionCount, 0)
        XCTAssertEqual(tracker.lastAcceptedStateVersion, 12)
    }

    func testTrackerRejectsRegressionWithoutAcceptingIt() throws {
        let tracker = IOSStateVersionTracker()
        try tracker.accept(10)
        try tracker.accept(12)

        XCTAssertThrowsError(try tracker.accept(11))
        XCTAssertEqual(tracker.observationCount, 2)
        XCTAssertEqual(tracker.regressionCount, 1)
        XCTAssertEqual(tracker.lastAcceptedStateVersion, 12)
    }

    func testWriterCreatesSeparateDecodableFilesWithoutTemporaryArtifact() throws {
        let directory = try makeTemporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let writer = try IOSObservationWriter(directory: directory)

        try writer.record(observation(version: 10, event: "first"))
        try writer.record(observation(version: 11, event: "second"))

        let first = directory.appendingPathComponent("ios-observation-000001.json")
        let second = directory.appendingPathComponent("ios-observation-000002.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: first.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: second.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.appendingPathComponent(".ios-observation-000001.json.tmp").path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.appendingPathComponent(".ios-observation-000002.json.tmp").path))
        XCTAssertEqual(try JSONDecoder().decode(IOSStateVersionObservation.self, from: Data(contentsOf: first)).event, "first")
        XCTAssertEqual(try JSONDecoder().decode(IOSStateVersionObservation.self, from: Data(contentsOf: second)).event, "second")
    }

    func testWriterFailsClearlyOnExistingFilenameCollision() throws {
        let directory = try makeTemporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let target = directory.appendingPathComponent("ios-observation-000001.json")
        try Data("existing".utf8).write(to: target)
        let writer = try IOSObservationWriter(directory: directory)

        XCTAssertThrowsError(try writer.record(observation(version: 10, event: "first")))
        XCTAssertEqual(try Data(contentsOf: target), Data("existing".utf8))
    }

    func testDiagnosticMarkerIsAtomicAndWrittenOnlyOnce() throws {
        let directory = try makeTemporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let writer = try IOSObservationWriter(directory: directory)
        let first = observation(version: 41, event: "ios-photo-results-observed")
        let later = observation(version: 42, event: "must-not-overwrite")

        try writer.writeMarkerOnce("ios-photo-results-observed", observation: first)
        try writer.writeMarkerOnce("ios-photo-results-observed", observation: later)

        let target = directory.appendingPathComponent("ios-photo-results-observed")
        let json = try JSONSerialization.jsonObject(with: Data(contentsOf: target)) as? [String: Any]
        XCTAssertEqual(json?["event"] as? String, "ios-photo-results-observed")
        XCTAssertEqual(json?["stateVersion"] as? Int, 41)
        XCTAssertEqual(json?["gameStage"] as? String, "Lobby")
        XCTAssertEqual(json?["connectionState"] as? String, "Unknown")
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.appendingPathComponent(".ios-photo-results-observed.tmp").path))
    }

    func testWriterFailsClearlyForMissingCoordinationDirectory() {
        let missing = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("partygame-ios-observation-missing-\(UUID().uuidString)")

        XCTAssertThrowsError(try IOSObservationWriter(directory: missing))
    }

    private func observation(version: Int64, event: String) -> IOSStateVersionObservation {
        IOSStateVersionObservation(event: event, stateVersion: version, phase: "Lobby", questionId: "", timestampUtc: "2026-07-28T00:00:00Z")
    }

    private func makeTemporaryDirectory() throws -> URL {
        let directory = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("partygame-ios-observation-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false)
        return directory
    }
}
