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
        try playFullGame()
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

    private func playFullGame() throws {
        var submitted = Set<String>()
        var voted = Set<String>()
        let deadline = Date().addingTimeInterval(240)

        while Date() < deadline {
            if app.otherElements["game-completed-view"].exists {
                mark("ios-completed-observed")
                return
            }

            if !submitted.contains("playerselection"), app.buttons["E2E Host"].exists {
                app.buttons["E2E Host"].tap()
                try waitUntil(timeout: 10, description: "zapis wyboru gracza") { !self.app.buttons["E2E Host"].isEnabled }
                submitted.insert("playerselection")
                mark("ios-player-selection-submitted")
            } else if !submitted.contains("textanswer"), app.textViews["textanswer.input"].exists {
                let input = app.textViews["textanswer.input"]
                input.tap()
                input.typeText("Odpowiedź iPhone")
                app.buttons["textanswer.submit"].tap()
                if app.staticTexts["textanswer.waiting"].waitForExistence(timeout: 5) {
                    submitted.insert("textanswer")
                    mark("ios-text-submitted")
                } else {
                    submitted.insert("textanswer")
                    mark("ios-text-subject-observed")
                }
            } else if !submitted.contains("photoanswer"), app.buttons["photoAnswer.chooseLibrary"].exists {
                app.buttons["photoAnswer.chooseLibrary"].tap()
                try chooseImportedPhoto()
                try waitFor(app.buttons["photoAnswer.usePhoto"], timeout: 15, description: "przycisk wysłania zdjęcia odpowiedzi").tap()
                try waitFor(app.otherElements["photoAnswer.waiting"], timeout: 15, description: "zapis zdjęcia odpowiedzi")
                submitted.insert("photoanswer")
                mark("ios-photo-submitted")
            } else if !submitted.contains("drawinganswer"), app.buttons["drawing.start"].exists {
                app.buttons["drawing.start"].tap()
                let canvas = try waitFor(app.otherElements["drawing-canvas"], timeout: 10, description: "canvas rysowania")
                canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.25, dy: 0.25))
                    .press(forDuration: 0.1, thenDragTo: canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.75, dy: 0.7)))
                try waitFor(app.buttons["drawing.done"], description: "podgląd rysunku").tap()
                try waitFor(app.buttons["drawing-submit-button"], timeout: 10, description: "wysłanie rysunku").tap()
                try waitFor(app.otherElements["drawing-waiting-state"], timeout: 15, description: "zapis rysunku")
                submitted.insert("drawinganswer")
                mark("ios-drawing-submitted")
            } else if !voted.contains("textanswer"), let option = firstEnabled(prefix: "textanswer.vote_option") {
                option.tap()
                try waitUntil(timeout: 10, description: "zapis głosu tekstowego") {
                    !self.app.buttons.matching(identifier: "textanswer.vote_option").firstMatch.exists
                }
                voted.insert("textanswer")
                mark("ios-text-voted")
            } else if !voted.contains("photoanswer"), firstEnabled(prefix: "photoAnswer.option.") != nil {
                try submitFirstAcceptedVote(
                    optionPrefix: "photoAnswer.option.",
                    voteButtonIdentifier: "photoAnswer.vote",
                    description: "głos na zdjęcie"
                )
                voted.insert("photoanswer")
                mark("ios-photo-voted")
            } else if !voted.contains("drawinganswer"), firstEnabled(prefix: "drawing-voting-option-") != nil {
                try submitFirstAcceptedVote(
                    optionPrefix: "drawing-voting-option-",
                    voteButtonIdentifier: "drawing.vote",
                    description: "głos na rysunek"
                )
                voted.insert("drawinganswer")
                mark("ios-drawing-voted")
            }
            RunLoop.current.run(until: Date().addingTimeInterval(0.15))
        }
        XCTFail("Timeout: pełny przebieg gry nie doszedł do Completed.\n\(app.debugDescription)")
    }

    private func firstEnabled(prefix: String) -> XCUIElement? {
        let candidates = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", prefix))
            .allElementsBoundByIndex
        return candidates.first(where: { $0.exists && $0.isEnabled })
    }

    private func submitFirstAcceptedVote(
        optionPrefix: String,
        voteButtonIdentifier: String,
        description: String
    ) throws {
        let options = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", optionPrefix))
            .allElementsBoundByIndex
        for option in options where option.exists && option.isEnabled {
            option.tap()
            let voteButton = app.buttons[voteButtonIdentifier]
            guard voteButton.waitForExistence(timeout: 2), voteButton.isEnabled else { continue }
            voteButton.tap()
            if waitUntilResult(timeout: 5, predicate: { !voteButton.exists }) { return }
        }
        XCTFail("Nie udało się zapisać \(description) na żadną dozwoloną opcję.\n\(app.debugDescription)")
        throw NSError(domain: "MixedGameClientE2E", code: 5)
    }

    private func waitUntilResult(timeout: TimeInterval, predicate: @escaping () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if predicate() { return true }
            RunLoop.current.run(until: Date().addingTimeInterval(0.1))
        }
        return false
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
