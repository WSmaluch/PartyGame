import XCTest

final class PhotoAnswerUITests: XCTestCase {
    private func launch(_ scenario: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = [scenario, "-AppleLanguages", "(pl)", "-AppleLocale", "pl_PL"]
        app.launch()
        XCTAssertTrue(app.otherElements["game.started"].waitForExistence(timeout: 5))
        return app
    }

    func test01TaskScreen() { XCTAssertTrue(launch("-uiTestingPhotoCapture").buttons["photoAnswer.takePhoto"].exists) }

    func test02CameraUnavailableOnSimulatorShowsFallback() {
        let app = launch("-uiTestingPhotoCameraUnavailable")
        app.buttons["photoAnswer.takePhoto"].tap()
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] %@", "niedostępny")).firstMatch.waitForExistence(timeout: 2))
    }

    func test03LibraryPickerEntryIsAvailable() { XCTAssertTrue(launch("-uiTestingPhotoCapture").buttons["photoAnswer.chooseLibrary"].exists) }

    func test04PreviewRequiresExplicitAcceptance() { XCTAssertTrue(launch("-uiTestingPhotoPreview").buttons["photoAnswer.usePhoto"].exists) }

    func test05UploadProgressIsVisible() {
        let app = launch("-uiTestingPhotoUpload")
        XCTAssertTrue(app.progressIndicators.firstMatch.exists)
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "42")).firstMatch.exists)
    }

    func test06UploadSuccessRoutesToWaiting() { XCTAssertTrue(launch("-uiTestingPhotoWaiting").staticTexts["Zdjęcie wysłane"].exists) }

    func test07WaitingShowsConfirmation() {
        XCTAssertTrue(launch("-uiTestingPhotoWaiting").staticTexts["Zdjęcie wysłane"].exists)
    }

    func test08AnonymousVotingGallery() { XCTAssertTrue(launch("-uiTestingPhotoVoting").buttons["photoAnswer.vote"].exists) }

    func test09OwnPhotoIsMarked() { XCTAssertTrue(launch("-uiTestingPhotoVoting").staticTexts["Twoje zdjęcie"].exists) }

    func test10SelfVoteCanBeSelected() {
        let app = launch("-uiTestingPhotoVoting")
        let ownCard = app.descendants(matching: .any)["photoAnswer.option.40000000-0000-0000-0000-000000000001"]
        XCTAssertTrue(ownCard.exists)
        ownCard.tap()
        let enabled = NSPredicate(format: "isEnabled == true")
        expectation(for: enabled, evaluatedWith: app.buttons["photoAnswer.vote"])
        waitForExpectations(timeout: 2)
    }

    func test11VoteWaiting() { XCTAssertTrue(launch("-uiTestingPhotoVoteWaiting").staticTexts["Głos zapisany"].exists) }

    func test12ResultsShowBackendAuthorAndPoints() {
        let app = launch("-uiTestingPhotoResults")
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "Ola")).firstMatch.exists)
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "+100")).firstMatch.exists)
    }

    func test13ZeroPhotos() { XCTAssertTrue(launch("-uiTestingPhotoZero").staticTexts["Nikt nie przesłał zdjęcia"].exists) }

    func test14OnePhotoHasNoVoterSection() {
        let app = launch("-uiTestingPhotoOne")
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS %@", "Ola")).firstMatch.exists)
        XCTAssertFalse(app.staticTexts["Głosowali"].exists)
    }

    func test15TieHighlightsAllBackendWinners() {
        XCTAssertEqual(launch("-uiTestingPhotoTie").staticTexts.matching(identifier: "photoAnswer.topResult").count, 2)
    }

    func test16ReconnectFixtureRestoresSubmittedState() { XCTAssertFalse(launch("-uiTestingPhotoWaiting").buttons["photoAnswer.takePhoto"].exists) }

    func test17DisplayPauseBlocksPhotoActions() {
        let app = launch("-uiTestingPhotoPaused")
        XCTAssertFalse(app.buttons["photoAnswer.takePhoto"].exists)
        XCTAssertFalse(app.buttons["photoAnswer.vote"].exists)
    }
}
