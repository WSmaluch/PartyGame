import SwiftUI

struct PartyButton: View {
    let titleKey: LocalizedStringKey
    let colors: [Color]

    var body: some View {
        Text(titleKey)
            .font(.title3.bold())
            .frame(maxWidth: .infinity)
            .padding(.vertical, 18)
            .foregroundStyle(.white)
            .background(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
            .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
            .shadow(color: colors.last?.opacity(0.3) ?? .clear, radius: 14, y: 8)
    }
}
