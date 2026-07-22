import Foundation

protocol HealthAPIClientProtocol {
    func fetchHealth(from url: URL) async throws -> HealthResponse
}

struct HealthAPIClient: HealthAPIClientProtocol {
    private let session: URLSession
    private let decoder: JSONDecoder

    init(session: URLSession = .shared, decoder: JSONDecoder = JSONDecoder()) {
        self.session = session
        self.decoder = decoder
    }

    func fetchHealth(from url: URL) async throws -> HealthResponse {
        var request = URLRequest(url: url)
        request.timeoutInterval = 10
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        do {
            let (data, response) = try await session.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse else {
                throw HealthAPIError.invalidResponse
            }
            guard (200 ... 299).contains(httpResponse.statusCode) else {
                throw HealthAPIError.httpStatus(httpResponse.statusCode)
            }

            do {
                return try decoder.decode(HealthResponse.self, from: data)
            } catch {
                throw HealthAPIError.invalidJSON
            }
        } catch is CancellationError {
            throw HealthAPIError.cancelled
        } catch let error as URLError {
            switch error.code {
            case .cancelled:
                throw HealthAPIError.cancelled
            case .timedOut:
                throw HealthAPIError.timeout
            case .notConnectedToInternet, .networkConnectionLost, .cannotConnectToHost,
                 .cannotFindHost, .dnsLookupFailed:
                throw HealthAPIError.networkUnavailable
            default:
                throw HealthAPIError.transport(error)
            }
        } catch let error as HealthAPIError {
            throw error
        }
    }
}

enum HealthAPIError: LocalizedError {
    case cancelled
    case timeout
    case httpStatus(Int)
    case invalidResponse
    case invalidJSON
    case networkUnavailable
    case transport(Error)

    var errorDescription: String? {
        switch self {
        case .cancelled: String(localized: "error.request_cancelled")
        case .timeout: String(localized: "error.timeout")
        case let .httpStatus(status): String(format: String(localized: "error.http_status"), status)
        case .invalidResponse: String(localized: "error.invalid_response")
        case .invalidJSON: String(localized: "error.invalid_json")
        case .networkUnavailable: String(localized: "error.network_unavailable")
        case let .transport(error): error.localizedDescription
        }
    }
}
