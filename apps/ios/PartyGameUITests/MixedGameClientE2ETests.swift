import XCTest

final class MixedGameClientE2ETests: XCTestCase {
    private var app: XCUIApplication!
    private var environment: [String: String]!

    func testMixedGameClientE2E() throws {
        guard ProcessInfo.processInfo.environment["PARTYGAME_E2E_MODE"] == "1" else { return }
        environment = ProcessInfo.processInfo.environment
        let backend = try required("PARTYGAME_E2E_BACKEND_URL")
        let roomCode = try required("PARTYGAME_E2E_ROOM_CODE")
        let nickname = try required("PARTYGAME_E2E_PLAYER_NICKNAME")

        app = XCUIApplication()
        app.launchEnvironment = ["PARTYGAME_E2E_BACKEND_URL": backend]
        app.launch()
        mark("ios-launched")

        try waitFor(app.buttons["home.joinGame"], description: "normalny przycisk Dołącz na ekranie startowym").tap()
        try waitFor(app.textFields["join.roomCode"], description: "pole kodu pokoju").tap()
        app.textFields["join.roomCode"].typeText(roomCode)
        app.textFields["join.nickname"].tap()
        app.textFields["join.nickname"].typeText(nickname)
        app.buttons["join.submit"].tap()

        try waitFor(app.otherElements["profile-photo-actions"], timeout: 15, description: "akcje zdjęcia profilowego po dołączeniu")
        app.buttons["choose-profile-photo-button"].tap()
        try chooseImportedPhoto()
        try waitFor(app.otherElements["profile-photo-preview"], description: "podgląd wybranego zdjęcia")
        XCTAssertFalse(app.staticTexts["profile-photo-required-error"].exists, "Wybranie zdjęcia z galerii nie może zakończyć się błędem profilu.\n\(app.debugDescription)")
        try waitFor(app.buttons["save-profile-button"], description: "przycisk zapisu wybranego zdjęcia").tap()
        mark("ios-profile-selected")

        try waitFor(app.buttons["lobby.ready"], timeout: 20, description: "Lobby po produkcyjnym zapisie profilu")
        XCTAssertTrue(app.staticTexts[nickname].exists, "Lobby nie pokazuje gracza iOS o oczekiwanym nicku.\n\(app.debugDescription)")
        mark("ios-profile-saved")
        let readyButton = app.buttons["lobby.ready"]
        let labelBeforeReady = readyButton.label
        readyButton.tap()
        try waitUntil(timeout: 15, description: "potwierdzenie produkcyjnego Ready w lobby") {
            readyButton.exists && readyButton.label != labelBeforeReady
        }
        mark("ios-ready")
        guard environment["PARTYGAME_E2E_REQUIRE_GAME_STARTED"] == "1" else { return }
        try waitForMarker("game-started", timeout: 45, description: "potwierdzenie pojedynczego startu gry przez orkiestrator")
        mark("ios-observed-game-start")
    }

    @discardableResult
    private func waitFor(_ element: XCUIElement, timeout: TimeInterval = 10, description: String) throws -> XCUIElement {
        guard element.waitForExistence(timeout: timeout) else {
            XCTFail("Timeout: oczekiwano \(description).\n\(app.debugDescription)")
            throw NSError(domain: "MixedGameClientE2E", code: 1)
        }
        return element
    }

    private func required(_ name: String) throws -> String {
        guard let value = environment[name], !value.isEmpty else {
            XCTFail("Brak wymaganej konfiguracji launch environment: \(name)")
            throw NSError(domain: "MixedGameClientE2E", code: 2)
        }
        return value
    }

    private func chooseImportedPhoto() throws {
        let photos = XCUIApplication(bundleIdentifier: "com.apple.mobileslideshow")
        let candidate = photos.cells.firstMatch.exists ? photos.cells.firstMatch : app.cells.firstMatch
        try waitFor(candidate, timeout: 15, description: "pierwszy zaimportowany obraz w systemowym PhotosPicker").tap()
    }

    private func waitUntil(timeout: TimeInterval, description: String, predicate: @escaping () -> Bool) throws {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if predicate() { return }
            RunLoop.current.run(until: Date().addingTimeInterval(0.1))
        }
        XCTFail("Timeout: oczekiwano \(description).\n\(app.debugDescription)")
        throw NSError(domain: "MixedGameClientE2E", code: 3)
    }

    private func waitForMarker(_ name: String, timeout: TimeInterval, description: String) throws {
        guard let directory = environment["PARTYGAME_E2E_COORDINATION_DIR"], !directory.isEmpty else {
            throw NSError(domain: "MixedGameClientE2E", code: 4)
        }
        try waitUntil(timeout: timeout, description: description) {
            FileManager.default.fileExists(atPath: URL(fileURLWithPath: directory).appendingPathComponent(name).path)
        }
    }

    private func mark(_ name: String) {
        guard let directory = environment["PARTYGAME_E2E_COORDINATION_DIR"], !directory.isEmpty else { return }
        FileManager.default.createFile(atPath: URL(fileURLWithPath: directory).appendingPathComponent(name).path, contents: Data())
    }
}
