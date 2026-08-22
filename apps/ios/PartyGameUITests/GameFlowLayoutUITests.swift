import XCTest

final class GameFlowLayoutUITests: XCTestCase {
    func testSelectionCardsUseUsableWidthInsideTypicalIPhoneViewport() {
        let app = launch("-uiTestingGameScreenSelection")
        let card = app.descendants(matching: .any)["selection-player-38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA"]
        assertUsableCard(card, in: app)
    }

    func testSelectionIncludesOwnPlayerAsAVotingOption() {
        let app = launch("-uiTestingGameScreenSelection")
        let ownCard = app.descendants(matching: .any)["selection-player-0DC81D35-C68D-47C6-AEBB-5E86407A1BB0"]
        assertUsableCard(ownCard, in: app)
    }

    func testResultsCardsUseUsableWidthInsideTypicalIPhoneViewport() {
        let app = launch("-uiTestingGameScreenResults")
        let card = app.descendants(matching: .any)["selection-result-0DC81D35-C68D-47C6-AEBB-5E86407A1BB0"]
        assertUsableCard(card, in: app)
    }

    func testRoundSummaryEntriesFitInsideTypicalIPhoneViewport() {
        let app = launch("-uiTestingGameScreenRoundSummary")
        let entry = app.descendants(matching: .any)["game-ranking-entry-38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA"]
        assertUsableCard(entry, in: app)
        XCTAssertTrue(app.staticTexts["#1"].exists)
        XCTAssertTrue(app.descendants(matching: .any)["game-ranking-entry-71A8C49F-1A2B-418F-A5CD-7D47C9BC9280"].exists)
    }

    func testCompletedEntriesFitInsideTypicalIPhoneViewport() {
        let app = launch("-uiTestingGameScreenCompleted")
        XCTAssertTrue(app.descendants(matching: .any)["game-completed-view"].waitForExistence(timeout: 5))
        let entry = app.descendants(matching: .any)["game-ranking-entry-38C92C29-2CF5-49E0-BC6B-AEBF9F37BCCA"]
        assertUsableCard(entry, in: app)
        let rankingCard = app.descendants(matching: .any)["game-completed-ranking-card"]
        XCTAssertTrue(rankingCard.waitForExistence(timeout: 5))
        XCTAssertLessThan(rankingCard.frame.height, app.frame.height * 0.55, "Ranking card must size to its content instead of occupying the screen")
        let playAgain = app.buttons["game.play-again"]
        XCTAssertTrue(playAgain.waitForExistence(timeout: 5))
        XCTAssertTrue(playAgain.isHittable, "The completed-screen bottom action must remain visible and tappable")
    }

    func testCompletedParticipantSeesWaitingStateInsteadOfHostAction() {
        let app = launch("-uiTestingGameScreenCompletedParticipant")
        XCTAssertTrue(app.staticTexts["game-waiting-for-host"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["game.play-again"].exists)
    }

    private func launch(_ scenario: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = [scenario, "-AppleLanguages", "(pl)", "-AppleLocale", "pl_PL"]
        app.launch()
        XCTAssertTrue(app.otherElements["game.started"].waitForExistence(timeout: 5))
        return app
    }

    private func assertUsableCard(_ card: XCUIElement, in app: XCUIApplication, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(card.waitForExistence(timeout: 5), file: file, line: line)
        let viewport = app.frame
        let frame = card.frame
        XCTAssertGreaterThan(frame.width, viewport.width * 0.25, "Card is implausibly narrow", file: file, line: line)
        XCTAssertLessThan(frame.height, viewport.height * 0.45, "Card is implausibly tall", file: file, line: line)
        XCTAssertGreaterThanOrEqual(frame.minY, viewport.minY, "Card starts above the viewport", file: file, line: line)
        XCTAssertLessThanOrEqual(frame.maxY, viewport.maxY, "Card extends below the viewport", file: file, line: line)
    }
}
