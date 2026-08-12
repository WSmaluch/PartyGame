import Foundation
import XCTest

final class GameFlowLocalizationTests: XCTestCase {
    private let requiredKeys = [
        "action.submit", "category.intro.title", "common.cancel", "common.retry",
        "drawing.canvas", "drawing.clear", "drawing.clear.confirm", "drawing.done", "drawing.eraser",
        "drawing.imageUnavailable", "drawing.loading", "drawing.nobody", "drawing.own", "drawing.privateState.error",
        "drawing.redo", "drawing.revealOnDisplay", "drawing.saving", "drawing.send", "drawing.sent", "drawing.top",
        "drawing.undo", "drawing.vote", "drawing.voteSaved", "drawing.voters", "drawing.waiting",
        "game.awaiting_stage", "game.completed", "game.loading", "game.paused_for_display",
        "host.question_types", "question_type.drawing_answer", "question_type.photo_answer",
        "question_type.player_selection", "question_type.text_answer",
        "photoAnswer.cameraDenied", "photoAnswer.cameraUnavailable", "photoAnswer.chooseLibrary", "photoAnswer.chooseOther",
        "photoAnswer.choosePhoto", "photoAnswer.fullPhoto", "photoAnswer.imageUnavailable", "photoAnswer.loadingPrivateState",
        "photoAnswer.nobodySubmitted", "photoAnswer.openSettings", "photoAnswer.ownPhoto", "photoAnswer.preview", "photoAnswer.revealOnDisplay",
        "photoAnswer.savedConfirmation", "photoAnswer.saving", "photoAnswer.sent", "photoAnswer.submissionProgress", "photoAnswer.takeOther",
        "photoAnswer.takePhoto", "photoAnswer.topVotes", "photoAnswer.uploadingPercent", "photoAnswer.usePhoto", "photoAnswer.vote",
        "photoAnswer.voteSaved", "photoAnswer.voters", "photoAnswer.waitingPlayers", "question.intro.title", "results.no_data", "results.title",
        "round.summary.no_data", "round.summary.title", "score.points", "score.points.awarded", "selection.prompt",
        "textanswer.author", "textanswer.no_results", "textanswer.own_answer_marker", "textanswer.points_awarded", "textanswer.results_title",
        "textanswer.revealing", "textanswer.vote_count", "textanswer.vote_prompt", "textanswer.vote_waiting", "textanswer.voted_by",
        "textanswer.waiting", "timer.waitingForServer"
    ]

    func testGameFlowLocalizationKeysHaveTranslatedPolishAndEnglishValues() throws {
        let catalog = try catalogStrings()
        for key in requiredKeys {
            let entry = try XCTUnwrap(catalog[key] as? [String: Any], "Missing game-flow localization key: \(key)")
            let localizations = try XCTUnwrap(entry["localizations"] as? [String: Any], "Missing localizations for: \(key)")
            for language in ["pl", "en"] {
                let languageEntry = try XCTUnwrap(localizations[language] as? [String: Any], "Missing \(language) localization for: \(key)")
                let stringUnit = try XCTUnwrap(languageEntry["stringUnit"] as? [String: Any], "Missing string unit for \(language): \(key)")
                let value = try XCTUnwrap(stringUnit["value"] as? String, "Missing value for \(language): \(key)")
                XCTAssertFalse(value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, "Empty \(language) localization for: \(key)")
                XCTAssertNotEqual(value, key, "Raw localization key rendered for \(language): \(key)")
            }
        }
    }

    private func catalogStrings() throws -> [String: Any] {
        let catalogURL = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("PartyGame/Localization/Localizable.xcstrings")
        let data = try Data(contentsOf: catalogURL)
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        return try XCTUnwrap(root["strings"] as? [String: Any])
    }
}
