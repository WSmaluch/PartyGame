import SwiftUI

struct CountdownTimerView: View {
    let stageEndsAtUtc: String?
    let serverOffset: TimeInterval

    var body: some View {
        if let endsAtStr = stageEndsAtUtc,
           let endsAt = ISO8601DateFormatter().date(from: endsAtStr) {
            TimelineView(.animation) { context in
                let now = context.date.addingTimeInterval(serverOffset)
                let remaining = max(0, endsAt.timeIntervalSince(now))
                
                VStack(spacing: 4) {
                    Text(String(format: "%.1fs", remaining))
                        .font(.largeTitle.monospacedDigit())
                    if remaining == 0 { Text("timer.waitingForServer").font(.caption).foregroundStyle(.secondary) }
                }
            }
        } else {
            Text("--")
        }
    }
}
