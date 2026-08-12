import XCTest

final class DrawingAnswerUITests: XCTestCase {
    private func launch(_ scenario: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = [scenario, "-AppleLanguages", "(pl)", "-AppleLocale", "pl_PL"]
        app.launch()
        XCTAssertTrue(app.otherElements["game.started"].waitForExistence(timeout: 5))
        return app
    }

    func test01TaskOpensEmptyCanvasWithDisabledPreview() {
        let app = launch("-uiTestingDrawingCapture")
        XCTAssertTrue(app.buttons["drawing.start"].exists)
        app.buttons["drawing.start"].tap()
        XCTAssertTrue(app.descendants(matching: .any)["drawing-canvas"].waitForExistence(timeout: 2))
        XCTAssertFalse(app.buttons["drawing.done"].isEnabled)
    }

    func test02DrawingEnablesPreviewAndUndo() {
        let app = launch("-uiTestingDrawingCapture")
        app.buttons["drawing.start"].tap()
        let canvas = app.descendants(matching: .any)["drawing-canvas"]
        canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.2, dy: 0.2))
            .press(forDuration: 0.05, thenDragTo: canvas.coordinate(withNormalizedOffset: CGVector(dx: 0.8, dy: 0.8)))
        XCTAssertTrue(app.buttons["drawing.done"].isEnabled)
        XCTAssertTrue(app.buttons["drawing.undo"].isEnabled)
    }

    func test03ColorsWidthsAndEraserAreAccessible() {
        let app = launch("-uiTestingDrawingCapture")
        app.buttons["drawing.start"].tap()
        XCTAssertTrue(app.buttons["drawing.color.red"].exists)
        XCTAssertTrue(app.buttons["drawing.color.blue"].exists)
        XCTAssertTrue(app.buttons["drawing.width.4.0"].exists)
        XCTAssertTrue(app.buttons["drawing.width.22.0"].exists)
        XCTAssertTrue(app.buttons["drawing.eraser"].exists)
    }

    func test04UndoRedoAndClearConfirmation() {
        let app = launch("-uiTestingDrawingCapture")
        app.buttons["drawing.start"].tap()
        let canvas = app.descendants(matching: .any)["drawing-canvas"]
        canvas.tap()
        app.buttons["drawing.undo"].tap()
        XCTAssertTrue(app.buttons["drawing.redo"].isEnabled)
        app.buttons["drawing.redo"].tap()
        app.buttons["drawing.clear"].tap()
        XCTAssertTrue(app.alerts.firstMatch.waitForExistence(timeout: 2))
    }

    func test05PreviewKeepsEditingAndSendAvailable() {
        let app = launch("-uiTestingDrawingPreview")
        XCTAssertTrue(app.descendants(matching: .any)["drawing-canvas"].exists)
        XCTAssertTrue(app.buttons["drawing-submit-button"].exists)
        XCTAssertTrue(app.buttons["drawing.undo"].exists)
    }

    func test06UploadProgressAndRetryStates() {
        let upload = launch("-uiTestingDrawingUpload")
        XCTAssertTrue(upload.progressIndicators.firstMatch.exists)
        upload.terminate()
        let retry = launch("-uiTestingDrawingRetry")
        XCTAssertTrue(retry.buttons["drawing.retry"].exists)
    }

    func test07WaitingAndReconnectRestoreSubmittedState() {
        let app = launch("-uiTestingDrawingWaiting")
        XCTAssertTrue(app.staticTexts["Rysunek wysłany"].exists)
        XCTAssertFalse(app.buttons["drawing.start"].exists)
    }

    func test08RevealHasNoAuthorOrVotingAction() {
        let app = launch("-uiTestingDrawingReveal")
        XCTAssertFalse(app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] %@", "Autor")).firstMatch.exists)
        XCTAssertFalse(app.buttons["drawing.vote"].exists)
    }

    func test09VotingMarksOwnDrawingAndAllowsSelfVote() {
        let app = launch("-uiTestingDrawingVoting")
        XCTAssertTrue(app.staticTexts["Twój rysunek"].exists)
        let own = app.buttons["drawing-voting-option-50000000-0000-0000-0000-000000000001"]
        XCTAssertTrue(own.exists)
        own.tap()
        XCTAssertTrue(app.buttons["drawing.vote"].isEnabled)
    }

    func test10VoteWaitingRestoresPrivateState() {
        XCTAssertTrue(launch("-uiTestingDrawingVoteWaiting").staticTexts["drawing.voteSaved"].exists)
    }

    func test11ResultsShowAuthorsVotersAndPoints() {
        let app = launch("-uiTestingDrawingResults")
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "Ola")).firstMatch.exists)
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "+100")).firstMatch.exists)
    }

    func test12ZeroOneAndTieResults() {
        let zero = launch("-uiTestingDrawingZero")
        XCTAssertTrue(zero.staticTexts["Nikt nie przesłał rysunku"].exists)
        zero.terminate()
        let one = launch("-uiTestingDrawingOne")
        XCTAssertTrue(one.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "Ola")).firstMatch.exists)
        one.terminate()
        let tie = launch("-uiTestingDrawingTie")
        XCTAssertGreaterThanOrEqual(tie.staticTexts.matching(NSPredicate(format: "label CONTAINS[c] %@", "Najlepszy")).count, 2)
    }

    func test13PauseBlocksDrawingAndVotingActions() {
        let app = launch("-uiTestingDrawingPaused")
        XCTAssertFalse(app.buttons["drawing.start"].exists)
        XCTAssertFalse(app.buttons["drawing.vote"].exists)
    }
}
