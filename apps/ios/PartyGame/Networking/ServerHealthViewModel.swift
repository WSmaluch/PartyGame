import Foundation
import Observation

@MainActor
@Observable
final class ServerHealthViewModel {
    enum State: Equatable {
        case idle
        case loading
        case online(HealthResponse)
        case offline(String)
    }

    private let configuration: ServerConfiguration
    private let client: HealthAPIClientProtocol
    private var requestTask: Task<Void, Never>?

    private(set) var state: State = .idle

    init(configuration: ServerConfiguration, client: HealthAPIClientProtocol = HealthAPIClient()) {
        self.configuration = configuration
        self.client = client
    }

    func checkConnection() {
        requestTask?.cancel()
        state = .loading
        requestTask = Task {
            do {
                let response = try await client.fetchHealth(from: configuration.healthURL())
                guard !Task.isCancelled else { return }
                state = response.status == "ok"
                    ? .online(response)
                    : .offline(String(localized: "error.backend_not_ready"))
            } catch HealthAPIError.cancelled {
                return
            } catch is CancellationError {
                return
            } catch {
                state = .offline(error.localizedDescription)
            }
        }
    }

    func cancel() {
        requestTask?.cancel()
    }
}
