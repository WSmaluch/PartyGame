import XCTest

final class MixedGameClientE2ETests: XCTestCase {
    private var app: XCUIApplication!
    private var environment: [String: String]!
    private var observationWriter: IOSObservationWriter!
    private var drawingDiagnosticSequence = 0

    func testMixedGameClientE2E() throws {
        guard ProcessInfo.processInfo.environment["PARTYGAME_E2E_MODE"] == "1" else { return }
        environment = ProcessInfo.processInfo.environment
        let backend = try required("PARTYGAME_E2E_BACKEND_URL")
        let roomCode = try required("PARTYGAME_E2E_ROOM_CODE")
        let nickname = try required("PARTYGAME_E2E_PLAYER_NICKNAME")

        app = XCUIApplication()
        app.launchEnvironment = ["PARTYGAME_E2E_BACKEND_URL": backend]
        app.launch()
        observationWriter = try IOSObservationWriter(directory: coordinationDirectory())
        mark("ios-launched")

        try waitFor(app.buttons["home.joinGame"], description: "normalny przycisk Dołącz na ekranie startowym").tap()
        try waitFor(app.textFields["join.roomCode"], description: "pole kodu pokoju").tap()
        app.textFields["join.roomCode"].typeText(roomCode)
        app.textFields["join.nickname"].tap()
        app.textFields["join.nickname"].typeText(nickname)
        app.buttons["join.submit"].tap()

        let chooseProfilePhoto = app.buttons.matching(identifier: "profile-photo-actions").element(boundBy: 1)
        try waitFor(chooseProfilePhoto, timeout: 15, description: "wybór zdjęcia profilowego po dołączeniu").tap()
        try chooseImportedPhoto()
        try waitFor(app.images["profile-photo-preview"], description: "podgląd wybranego zdjęcia")
        XCTAssertFalse(app.staticTexts["profile-photo-required-error"].exists, "Wybranie zdjęcia z galerii nie może zakończyć się błędem profilu.\n\(app.debugDescription)")
        try waitFor(app.buttons["save-profile-button"], description: "przycisk zapisu wybranego zdjęcia").tap()
        mark("ios-profile-selected")

        try waitFor(app.buttons["lobby.ready"], timeout: 20, description: "Lobby po produkcyjnym zapisie profilu")
        let lobby = try recordCurrentSnapshot(event: "snapshot-lobby-accepted", stage: "lobby")
        XCTAssertEqual(lobby.phase, "Lobby")
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
        let started = try recordCurrentSnapshot(event: "snapshot-game-started", stage: "game-started")
        XCTAssertNotEqual(started.phase, "Lobby")
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
        let firstPhoto = app.images.matching(identifier: "PXGGridLayout-Info").firstMatch
        if firstPhoto.waitForExistence(timeout: 15), !firstPhoto.isHittable {
            // PhotosPicker's first-run overlay is hosted outside PartyGame and
            // does not expose its Close button to the app's accessibility query.
            app.coordinate(withNormalizedOffset: CGVector(dx: 0.91, dy: 0.20)).tap()
        }
        // PhotosUI exposes the photo as an accessibility image but does not
        // consistently mark it hittable. The top-left tile is visible after
        // dismissing onboarding, so tap its screen position directly.
        app.coordinate(withNormalizedOffset: CGVector(dx: 0.16, dy: 0.25)).tap()
    }

    private func playFullGame() throws {
        var submitted = Set<String>()
        var voted = Set<String>()
        var didReconnect = false
        var mustRecordPostReconnectAction = false
        var finalEditSubmissionCount = 0
        var drawingPrivateStateLoadingSince: Date?
        var diagnosedDrawingQuestions = Set<String>()
        var diagnosedTextVoteQuestions = Set<String>()
        let deadline = Date().addingTimeInterval(360)

        while Date() < deadline {
            // CompletedView exposes its root identifier on the title text.
            // Query the matching element type so this observation reflects the
            // rendered terminal state rather than an unrelated accessibility
            // container.
            if app.descendants(matching: .any)["game-completed-view"].exists {
                let terminal = try recordCurrentSnapshot(event: "ios-terminal-snapshot-received", stage: "completed")
                try observationWriter.writeMarkerOnce("ios-terminal-snapshot-received", observation: terminal)
                try waitUntil(timeout: 10, description: "trzy wyrenderowane pozycje rankingu") {
                    self.rankingEntryCount() == 3
                }
                let completed = try snapshot(from: app, event: "ios-completed-rendered")
                try observationWriter.writeMarkerOnce("ios-completed-rendered", observation: completed, rankingCount: rankingEntryCount())
                try observationWriter.writeMarkerOnce("ios-ranking-rendered", observation: completed, rankingCount: rankingEntryCount())
                mark("ios-completed-observed")
                return
            }

            var performedAction = false
            if let current = try? snapshot(from: app, event: "snapshot-stage-observed") {
                if current.phase == "showingPhotoAnswerResults" {
                    let photoResults = try snapshot(from: app, event: "ios-photo-results-observed")
                    try observationWriter.writeMarkerOnce("ios-photo-results-observed", observation: photoResults)
                }
                if current.phase == "completed" {
                    try observationWriter.writeMarkerOnce("ios-terminal-snapshot-received", observation: current)
                }
            }
            if let current = try? snapshot(from: app, event: "drawing-question-detected"),
               current.phase == "collectingDrawingAnswers",
               diagnosedDrawingQuestions.insert(current.questionId).inserted {
                drawingDiagnostic("drawing-question-detected", extra: [
                    "privateStateQuestion": current.questionId,
                    "lastUI": app.debugDescription
                ])
            }
            if let current = try? snapshot(from: app, event: "text-voting-detected"),
               current.phase == "collectingTextAnswerVotes",
               diagnosedTextVoteQuestions.insert(current.questionId).inserted {
                let textVoteOptionCount = app.buttons
                    .matching(NSPredicate(format: "identifier == %@", "textanswer.vote_option"))
                    .count
                drawingDiagnostic("text-voting-detected", extra: [
                    "privateStateQuestion": current.questionId,
                    "textVoteOptionCount": "\(textVoteOptionCount)",
                    "lastUI": app.debugDescription
                ])
            }
            if !submitted.contains("playerselection"), app.buttons["E2E Host"].exists {
                app.buttons["E2E Host"].tap()
                // The live player-selection control remains enabled until the
                // remaining players submit, so disabling is not a submission
                // acknowledgement. `submitted` prevents a second tap.
                submitted.insert("playerselection")
                performedAction = true
                mark("ios-player-selection-submitted")
            } else if !submitted.contains("textanswer"), app.textViews["textanswer.input"].exists {
                let input = app.textViews["textanswer.input"]
                input.tap()
                input.typeText("Odpowiedź iPhone")
                app.buttons["textanswer.submit"].tap()
                if app.staticTexts["textanswer.waiting"].waitForExistence(timeout: 5) {
                    submitted.insert("textanswer")
                    performedAction = true
                    mark("ios-text-submitted")
                } else {
                    submitted.insert("textanswer")
                    performedAction = true
                    mark("ios-text-subject-observed")
                }
            } else if !submitted.contains("textanswer"), app.staticTexts["textanswer.waiting"].exists {
                submitted.insert("textanswer")
                performedAction = true
                mark("ios-text-subject-observed")
            } else if !submitted.contains("textanswer"),
                      let current = try? snapshot(from: app, event: "snapshot-text-subject-observed"),
                      current.phase == "collectingTextAnswers" {
                // The planner excludes the selected subject from this answer
                // type.  The live snapshot is the source of truth when the
                // public UI has no submit affordance for that player.
                RunLoop.current.run(until: Date().addingTimeInterval(1))
                if !app.textViews["textanswer.input"].exists,
                   !app.staticTexts["textanswer.waiting"].exists {
                    submitted.insert("textanswer")
                    performedAction = true
                    mark("ios-text-subject-observed")
                }
            } else if !submitted.contains("finalselfie"),
                      let current = try? snapshot(from: app, event: "final-selfie-detected"),
                      current.phase == "collectingFinalSelfies",
                      app.otherElements["final-round-selfie-view"].exists,
                      app.buttons["photoAnswer.chooseLibrary"].exists {
                // The dedicated final-selfie root is rendered only after the
                // private SignalR/REST contract supplied a non-empty prompt.
                // Publish this before opening PhotosPicker so the orchestrator
                // can prove all three players are actionable while Display is 0/3.
                mark("ios-final-selfie-actionable-\(current.stateVersion)")
                app.buttons["photoAnswer.chooseLibrary"].tap()
                try chooseImportedPhoto()
                try waitFor(app.buttons["photoAnswer.usePhoto"], timeout: 15, description: "przycisk wysłania finałowego zdjęcia").tap()
                try waitFor(app.staticTexts["Photo sent"], timeout: 15, description: "zapis finałowego zdjęcia")
                try waitFor(app.staticTexts["Waiting for the other players"], timeout: 5, description: "oczekiwanie po zapisie finałowego zdjęcia")
                submitted.insert("finalselfie")
                performedAction = true
                mark("ios-final-selfie-submitted-\(current.stateVersion)")
            } else if let current = try? snapshot(from: app, event: "final-edit-detected"),
                      current.phase == "collectingFinalEdits",
                      app.buttons["final-round-edit-start"].exists {
                app.buttons["final-round-edit-start"].tap()
                let canvas = try waitFor(app.images["final-round-edit-canvas"], timeout: 10, description: "canvas finałowej edycji")
                canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.25, dy: 0.25))
                    .press(forDuration: 0.1, thenDragTo: canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.75, dy: 0.7)))
                try waitFor(app.buttons["final-round-edit-preview"], description: "podgląd finałowej edycji").tap()
                try waitFor(app.buttons["final-round-edit-send"], timeout: 10, description: "wysłanie finałowej edycji").tap()
                try waitFor(app.descendants(matching: .any)["final-round-waiting-view"], timeout: 15, description: "zapis finałowej edycji")
                performedAction = true
                finalEditSubmissionCount += 1
                mark("ios-final-edit-submitted-pass-\(finalEditSubmissionCount)")
            } else if let current = try? snapshot(from: app, event: "final-vote-detected"),
                      current.phase == "collectingFinalVotes",
                      firstEnabledButton(prefix: "final-round-vote-") != nil {
                try submitFirstAcceptedVote(
                    optionPrefix: "final-round-vote-",
                    voteButtonIdentifier: "final-round-vote-send",
                    description: "głos finałowy"
                )
                performedAction = true
                mark("ios-final-vote-submitted-\(current.stateVersion)")
            } else if !submitted.contains("photoanswer"), app.buttons["photoAnswer.chooseLibrary"].exists {
                app.buttons["photoAnswer.chooseLibrary"].tap()
                try chooseImportedPhoto()
                try waitFor(app.buttons["photoAnswer.usePhoto"], timeout: 15, description: "przycisk wysłania zdjęcia odpowiedzi").tap()
                try waitFor(app.staticTexts["Photo sent"], timeout: 15, description: "zapis zdjęcia odpowiedzi")
                try waitFor(app.staticTexts["Waiting for the other players"], timeout: 5, description: "stan oczekiwania po zapisie zdjęcia")
                submitted.insert("photoanswer")
                performedAction = true
                mark("ios-photo-submitted")
            } else if !submitted.contains("drawinganswer"), app.buttons["drawing.start"].exists {
                drawingDiagnostic("drawing-question-detected", extra: ["isEligible": "true", "hasSubmittedDrawingAnswer": "false"])
                app.buttons["drawing.start"].tap()
                let canvas = try waitFor(app.otherElements["drawing-canvas"], timeout: 10, description: "canvas rysowania")
                drawingDiagnostic("drawing-start-visible")
                canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.25, dy: 0.25))
                    .press(forDuration: 0.1, thenDragTo: canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.75, dy: 0.7)))
                drawingDiagnostic("drawing-gesture-completed")
                try waitFor(app.buttons["drawing.done"], description: "podgląd rysunku").tap()
                let submit = try waitFor(app.buttons["drawing-submit-button"], timeout: 10, description: "wysłanie rysunku")
                drawingDiagnostic("drawing-submit-tap")
                submit.tap()
                try waitFor(app.staticTexts["drawing-waiting-state"], timeout: 15, description: "zapis rysunku")
                drawingDiagnostic("drawing-submit-confirmed", extra: ["hasSubmittedDrawingAnswer": "true"])
                submitted.insert("drawinganswer")
                performedAction = true
                mark("ios-drawing-submitted")
                drawingDiagnostic("drawing-marker-written", extra: ["marker": "ios-drawing-submitted"])
            } else if !submitted.contains("drawinganswer"), app.staticTexts["drawing-waiting-state"].exists {
                drawingDiagnostic("drawing-question-detected", extra: ["isEligible": "false", "hasSubmittedDrawingAnswer": "false"])
                submitted.insert("drawinganswer")
                performedAction = true
                mark("ios-drawing-not-required")
                drawingDiagnostic("drawing-marker-written", extra: ["marker": "ios-drawing-not-required"])
            } else if !voted.contains("textanswer"), let option = firstEnabledButton(prefix: "textanswer.vote_option") {
                option.tap()
                try waitFor(app.staticTexts["textanswer.vote_waiting"], timeout: 10, description: "zapis głosu tekstowego")
                voted.insert("textanswer")
                performedAction = true
                mark("ios-text-voted")
            } else if !voted.contains("textanswer"), app.staticTexts["textanswer.vote_waiting"].exists {
                voted.insert("textanswer")
                performedAction = true
                mark("ios-text-vote-not-required")
            } else if !voted.contains("photoanswer"), firstEnabledButton(prefix: "photoAnswer.option.") != nil {
                try submitFirstAcceptedVote(
                    optionPrefix: "photoAnswer.option.",
                    voteButtonIdentifier: "photoAnswer.vote",
                    description: "głos na zdjęcie"
                )
                voted.insert("photoanswer")
                performedAction = true
                mark("ios-photo-voted")
            } else if !voted.contains("drawinganswer"), firstEnabledButton(prefix: "drawing-voting-option-") != nil {
                try submitFirstAcceptedVote(
                    optionPrefix: "drawing-voting-option-",
                    voteButtonIdentifier: "drawing.vote",
                    description: "głos na rysunek"
                )
                voted.insert("drawinganswer")
                performedAction = true
                mark("ios-drawing-voted")
            }
            if app.staticTexts["drawing.private-state.error"].exists {
                drawingDiagnostic("drawing-private-state-error")
                XCTFail("Nie udało się pobrać prywatnego stanu aktualnego pytania rysunkowego.\n\(app.debugDescription)")
            }
            if app.activityIndicators["drawing.private-state.loading"].exists {
                drawingDiagnostic("drawing-private-state-load-attempt")
                if drawingPrivateStateLoadingSince == nil { drawingPrivateStateLoadingSince = Date() }
                if Date().timeIntervalSince(drawingPrivateStateLoadingSince!) > 10 {
                    drawingDiagnostic("drawing-private-state-load-timeout", extra: ["lastUI": app.debugDescription])
                    XCTFail("Loader prywatnego stanu rysowania nie przeszedł do drawing.start.\n\(app.debugDescription)")
                }
            } else {
                drawingPrivateStateLoadingSince = nil
            }
            if !didReconnect, submitted.contains("finalselfie") {
                let before = try recordCurrentSnapshot(event: "snapshot-before-disconnect", stage: "before-disconnect")
                try observationWriter.writeMarkerOnce("ios-before-reconnect", observation: before)
                mark("ios-reconnect-requested")
                app.terminate()
                mark("ios-terminated")
                app.launch()
                mark("ios-relaunched")
                try waitUntil(timeout: 45, description: "production resume tego samego gracza") {
                    guard let recovered = try? self.snapshot(from: self.app, event: "snapshot-after-recovery") else { return false }
                    return recovered.stateVersion >= before.stateVersion
                }
                let recovered = try recordCurrentSnapshot(event: "snapshot-after-recovery", stage: "after-recovery")
                XCTAssertGreaterThanOrEqual(recovered.stateVersion, before.stateVersion)
                try observationWriter.writeMarkerOnce("ios-after-reconnect", observation: recovered)
                mark("ios-reconnected")
                mark("ios-recovered-state")
                didReconnect = true
                mustRecordPostReconnectAction = true
                continue
            }
            if mustRecordPostReconnectAction && performedAction {
                _ = try recordCurrentSnapshot(event: "snapshot-after-post-reconnect-action", stage: "post-reconnect-action")
                mustRecordPostReconnectAction = false
            }
            RunLoop.current.run(until: Date().addingTimeInterval(0.15))
        }
        XCTFail("Timeout: pełny przebieg gry nie doszedł do Completed.\n\(app.debugDescription)")
    }

    private func firstEnabledButton(prefix: String) -> XCUIElement? {
        let candidates = app.buttons
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", prefix))
            .allElementsBoundByIndex
        return candidates.first(where: { $0.exists && $0.isEnabled })
    }

    private func submitFirstAcceptedVote(
        optionPrefix: String,
        voteButtonIdentifier: String,
        description: String
    ) throws {
        let options = app.buttons
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

    private func drawingDiagnostic(_ event: String, extra: [String: String] = [:]) {
        guard let directory = try? coordinationDirectory() else { return }
        drawingDiagnosticSequence += 1
        let observed = try? snapshot(from: app, event: event)
        var payload: [String: Any] = [
            "event": event,
            "timestampUtc": ISO8601DateFormatter().string(from: Date()),
            "stateVersion": observed.map { $0.stateVersion } ?? NSNull(),
            "roomPhase": observed.map { $0.phase } ?? NSNull(),
            "questionId": observed.map { $0.questionId } ?? NSNull(),
            "drawingStartVisible": app.buttons["drawing.start"].exists,
            "drawingWaitingVisible": app.staticTexts["drawing-waiting-state"].exists,
            "privateStateLoadingVisible": app.activityIndicators["drawing.private-state.loading"].exists
        ]
        extra.forEach { payload[$0.key] = $0.value }
        let name = String(format: "drawing-diagnostic-%06d.json", drawingDiagnosticSequence)
        guard JSONSerialization.isValidJSONObject(payload), let data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys]) else { return }
        try? data.write(to: directory.appendingPathComponent(name), options: .atomic)
        print("MIXED_E2E_DRAWING \(event) \(payload)")
    }

    private func coordinationDirectory() throws -> URL {
        guard let path = environment["PARTYGAME_E2E_COORDINATION_DIR"], !path.isEmpty else {
            throw NSError(domain: "MixedGameClientE2E", code: 6, userInfo: [NSLocalizedDescriptionKey: "Brak PARTYGAME_E2E_COORDINATION_DIR dla ledgeru iOS."])
        }
        return URL(fileURLWithPath: path)
    }

    private func snapshot(from app: XCUIApplication, event: String) throws -> IOSStateVersionObservation {
        let prefix = "game.snapshot|"
        let elements = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", prefix))
        let identifier = elements.firstMatch.identifier
        guard identifier.hasPrefix(prefix) else {
            throw NSError(domain: "MixedGameClientE2E", code: 7, userInfo: [NSLocalizedDescriptionKey: "\(event): brak accessibility identifier snapshotu."])
        }
        return try IOSStateVersionObservation.parse(identifier: identifier, event: event, connectionState: connectionState())
    }

    private func recordCurrentSnapshot(event: String, stage: String) throws -> IOSStateVersionObservation {
        do { let value = try snapshot(from: app, event: event); try observationWriter.record(value); return value }
        catch { XCTFail("\(stage): \(error.localizedDescription)"); throw error }
    }

    private func connectionState() -> String {
        let prefix = "game.connection|state="
        let elements = app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", prefix))
        let identifier = elements.firstMatch.identifier
        return identifier.hasPrefix(prefix) ? String(identifier.dropFirst(prefix.count)) : "Unknown"
    }

    private func rankingEntryCount() -> Int {
        Set(app.descendants(matching: .any)
            .matching(NSPredicate(format: "identifier BEGINSWITH %@", "game-ranking-entry-"))
            .allElementsBoundByIndex
            .filter(\.exists)
            .map(\.identifier))
            .count
    }
}
