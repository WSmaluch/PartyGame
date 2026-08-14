import Foundation

struct MultipartFormData: Equatable, Sendable {
    let contentType: String
    let body: Data
}

enum MultipartFormDataBuilder {
    static func profilePhoto(jpegData: Data, boundary: String = "PartyGame-\(UUID().uuidString)") -> MultipartFormData {
        var body = Data()
        body.appendUTF8("--\(boundary)\r\n")
        body.appendUTF8("Content-Disposition: form-data; name=\"file\"; filename=\"profile.jpg\"\r\n")
        body.appendUTF8("Content-Type: image/jpeg\r\n\r\n")
        body.append(jpegData)
        body.appendUTF8("\r\n--\(boundary)--\r\n")
        return MultipartFormData(contentType: "multipart/form-data; boundary=\(boundary)", body: body)
    }

    static func photoAnswer(
        playerId: UUID,
        reconnectToken: String,
        clientSubmissionId: UUID,
        jpegData: Data,
        boundary: String = "PartyGame-\(UUID().uuidString)"
    ) -> MultipartFormData {
        var body = Data()
        func field(_ name: String, _ value: String) {
            body.appendUTF8("--\(boundary)\r\n")
            body.appendUTF8("Content-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n")
        }
        field("playerId", playerId.uuidString)
        field("reconnectToken", reconnectToken)
        field("clientSubmissionId", clientSubmissionId.uuidString)
        body.appendUTF8("--\(boundary)\r\n")
        body.appendUTF8("Content-Disposition: form-data; name=\"photo\"; filename=\"photo.jpg\"\r\n")
        body.appendUTF8("Content-Type: image/jpeg\r\n\r\n")
        body.append(jpegData)
        body.appendUTF8("\r\n--\(boundary)--\r\n")
        return MultipartFormData(contentType: "multipart/form-data; boundary=\(boundary)", body: body)
    }

    static func drawingAnswer(
        playerId: UUID,
        reconnectToken: String,
        clientSubmissionId: UUID,
        pngData: Data,
        boundary: String = "PartyGame-\(UUID().uuidString)"
    ) -> MultipartFormData {
        var body = Data()
        func field(_ name: String, _ value: String) {
            body.appendUTF8("--\(boundary)\r\n")
            body.appendUTF8("Content-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n")
        }
        field("playerId", playerId.uuidString)
        field("reconnectToken", reconnectToken)
        field("clientSubmissionId", clientSubmissionId.uuidString)
        body.appendUTF8("--\(boundary)\r\n")
        body.appendUTF8("Content-Disposition: form-data; name=\"drawing\"; filename=\"drawing.png\"\r\n")
        body.appendUTF8("Content-Type: image/png\r\n\r\n")
        body.append(pngData)
        body.appendUTF8("\r\n--\(boundary)--\r\n")
        return MultipartFormData(contentType: "multipart/form-data; boundary=\(boundary)", body: body)
    }

    static func finalRoundEdit(playerId: UUID, reconnectToken: String, clientSubmissionId: UUID, pngData: Data, boundary: String = "PartyGame-\(UUID().uuidString)") -> MultipartFormData {
        drawingAnswer(playerId: playerId, reconnectToken: reconnectToken, clientSubmissionId: clientSubmissionId, pngData: pngData, boundary: boundary)
    }
}

private extension Data {
    mutating func appendUTF8(_ value: String) {
        append(Data(value.utf8))
    }
}
