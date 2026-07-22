import SwiftUI

struct FeaturePlaceholderView: View {
    let titleKey: LocalizedStringKey
    let messageKey: LocalizedStringKey

    var body: some View {
        ContentUnavailableView {
            Label(titleKey, systemImage: "party.popper.fill")
        } description: {
            Text(messageKey)
        }
        .navigationTitle(titleKey)
    }
}
