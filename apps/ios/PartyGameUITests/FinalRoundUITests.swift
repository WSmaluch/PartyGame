import XCTest

final class FinalRoundUITests: XCTestCase {
    private func launch(_ scenario: String) -> XCUIApplication {
        let app = XCUIApplication(); app.launchArguments = [scenario, "-AppleLanguages", "(pl)", "-AppleLocale", "pl_PL"]; app.launch()
        XCTAssertTrue(app.otherElements["game.started"].waitForExistence(timeout: 5)); return app
    }
    func testSelfieAndEditAssignment() {
        let selfie = launch("-uiTestingFinalSelfie")
        XCTAssertTrue(selfie.descendants(matching: .any)["final-round-selfie-view"].exists)
        XCTAssertTrue(selfie.staticTexts["Pokaż groźną minę"].exists)
        XCTAssertTrue(selfie.buttons["photoAnswer.takePhoto"].exists)
        XCTAssertFalse(selfie.descendants(matching: .any)["final-round-selfie-private-state-loader"].exists)
        let edit = launch("-uiTestingFinalEdit")
        XCTAssertTrue(edit.buttons["final-round-edit-start"].exists)
        XCTAssertTrue(edit.staticTexts["Spraw, aby Jan wyglądał jak Kosmiczny pirat"].exists)
    }
    func testEditWaitingAndPresentation() { XCTAssertTrue(launch("-uiTestingFinalEditWaiting").otherElements["final-round-waiting-view"].exists); XCTAssertTrue(launch("-uiTestingFinalPresentation").otherElements["final-round-presentation-view"].exists) }
    func testVotingAndWaiting() { let app = launch("-uiTestingFinalVoting"); XCTAssertTrue(app.otherElements["final-round-voting-view"].exists); XCTAssertTrue(launch("-uiTestingFinalVoteWaiting").otherElements["final-round-waiting-view"].exists) }
    func testResultsSummaryAndCompleted() { XCTAssertTrue(launch("-uiTestingFinalResults").otherElements["final-round-results-view"].exists); XCTAssertTrue(launch("-uiTestingFinalSummary").otherElements["game-summary-view"].exists); XCTAssertTrue(launch("-uiTestingFinalCompleted").otherElements["game-completed-view"].exists) }
}
