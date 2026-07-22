import Foundation

struct LocalPlayerSession: Codable, Equatable, Sendable {
    let roomCode: String
    let playerId: UUID
    let nickname: String
    let isHost: Bool
    let serverBaseURL: String
}
