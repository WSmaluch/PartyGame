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
        app.buttons["lobby.ready"].tap()
        mark("ios-ready")

        try waitFor(app.buttons["drawing.start"], timeout: 45, description: "DrawingAnswer po ustawieniu Ready")
        app.buttons["drawing.start"].tap()
        let canvas = try waitFor(app.otherElements["drawing-canvas"], description: "produkcyjny canvas DrawingAnswer")
        let start = canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.2, dy: 0.25))
        start.press(forDuration: 0.05, thenDragTo: canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.75)))
        XCTAssertTrue(app.buttons["drawing.done"].isEnabled, "Gest na canvasie nie utworzył rysunku.")
        app.buttons["drawing.done"].tap()
        try waitFor(app.buttons["drawing-submit-button"], description: "produkcyjny przycisk wysłania rysunku").tap()
        try waitFor(app.otherElements["drawing-waiting-state"], description: "stan oczekiwania po wysłaniu rysunku")
        mark("ios-drawing-submitted-1")
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

    private func mark(_ name: String) {
        guard let directory = environment["PARTYGAME_E2E_COORDINATION_DIR"], !directory.isEmpty else { return }
        FileManager.default.createFile(atPath: URL(fileURLWithPath: directory).appendingPathComponent(name).path, contents: Data())
    }
}
