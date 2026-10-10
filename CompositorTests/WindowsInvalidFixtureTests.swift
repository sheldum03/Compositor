import Foundation
import Testing
@testable import Compositor

@MainActor
struct WindowsInvalidFixtureTests {
    private struct Case: Decodable {
        let name: String
        let failure: String
    }

    @Test func fixedInvalidPackagesFailForTheirExpectedReason() async throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/windows/fixtures/invalid")
        let cases = try JSONDecoder().decode([Case].self, from: Data(contentsOf: root.appendingPathComponent("cases.json")))
        #expect(cases.count == 14)
        for item in cases {
            do {
                _ = try await ProjectStore.shared.load(from: root.appendingPathComponent(item.name + ".comp"))
                Issue.record("Invalid fixture accepted: \(item.name)")
            } catch {
                let actual: String
                switch error {
                case ProjectError.version(let version):
                    #expect(version == 99)
                    actual = "version"
                case ProjectError.invalid: actual = "invalid"
                case ProjectError.tooLarge: actual = "tooLarge"
                case ProjectError.missingImage: actual = "missingImage"
                case let error as CocoaError where error.code == .fileReadNoSuchFile: actual = "fileMissing"
                default: actual = "Unexpected: \(error)"
                }
                #expect(actual == item.failure, "\(item.name): \(actual)")
            }
        }
    }
}
