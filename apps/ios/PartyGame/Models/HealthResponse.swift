import Foundation

struct HealthResponse: Codable, Equatable {
    let status: String
    let service: String
    let version: String
    let utcTime: String
}
