import XCTest

final class PartyGameUITests: XCTestCase {
    func testHostAndJoinFormsAreUsableWithoutServer() {
        let app = XCUIApplication()
        app.launch()

        XCTAssertTrue(app.staticTexts["PartyGame"].waitForExistence(timeout: 5))
        app.buttons["home.hostGame"].tap()
        XCTAssertTrue(app.textFields["host.nickname"].exists)
        app.swipeUp()
        XCTAssertTrue(app.buttons["host.create"].exists)
        app.buttons["common.back"].tap()
        app.buttons["home.joinGame"].tap()
        XCTAssertTrue(app.textFields["join.roomCode"].exists)
        XCTAssertTrue(app.textFields["join.nickname"].exists)
    }

    func testLobbyFixtureShowsRoomPlayersAndStatusesWithoutServerOrCamera() {
        let app = XCUIApplication()
        app.launchArguments = ["-uiTestingLobby", "-AppleLanguages", "(pl)"]
        app.launch()

        XCTAssertTrue(app.staticTexts["lobby.roomCode"].waitForExistence(timeout: 5))
        XCTAssertEqual(app.staticTexts["lobby.roomCode"].label, "ABCD")
        XCTAssertTrue(app.staticTexts["Ola"].exists)
        XCTAssertTrue(app.staticTexts["Jan"].exists)
        XCTAssertTrue(app.buttons["lobby.ready"].exists)
    }

    func testRoomStartedFixtureRoutesToPlaceholder() {
        let app = XCUIApplication()
        app.launchArguments = ["-uiTestingStarted", "-AppleLanguages", "(pl)"]
        app.launch()

        XCTAssertTrue(app.otherElements["game.started"].waitForExistence(timeout: 5))
    }
}
