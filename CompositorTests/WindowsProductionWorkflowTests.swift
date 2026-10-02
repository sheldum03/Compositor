import AppKit
import Testing
@testable import Compositor

@MainActor
struct WindowsProductionWorkflowTests {
    private func pixels(_ image: CGImage) throws -> Data {
        let context = try BrushRaster.context(width: image.width, height: image.height, mask: false)
        BrushRaster.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height),
                         mask: false, context: context)
        let pointer = try #require(context.data)
        var result = Data()
        for row in 0..<image.height {
            result.append(Data(bytes: pointer.advanced(by: row * context.bytesPerRow), count: image.width * 4))
        }
        return result
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_FLAT_DIR"] != nil,
                   "Set WINDOWS_PRODUCTION_FLAT_DIR to the fixed two-layer production fixture."))
    func productionFlatNormalMatchesMacRenderer() async throws {
        let root = URL(fileURLWithPath: try #require(
            ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_FLAT_DIR"]))
        try await compareMacRender(root: root, masked: false,
                                   output: ProcessInfo.processInfo.environment["MAC_PRODUCTION_FLAT_OUTPUT_DIR"])
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_MASK_DIR"] != nil,
                   "Set WINDOWS_PRODUCTION_MASK_DIR to the fixed Gray8 production fixture."))
    func productionGrayMaskMatchesMacRenderer() async throws {
        let root = URL(fileURLWithPath: try #require(
            ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_MASK_DIR"]))
        try await compareMacRender(root: root, masked: true,
                                   output: ProcessInfo.processInfo.environment["MAC_PRODUCTION_MASK_OUTPUT_DIR"])
    }

    private func compareMacRender(root: URL, masked: Bool, output: String?) async throws {
        let reference = try #require(NSBitmapImageRep(
            data: Data(contentsOf: root.appendingPathComponent(
                masked ? "masked-composite-csharp.png" : "composite-csharp.png")))?.cgImage)
        let snapshot = try await ProjectStore.shared.load(from: root.appendingPathComponent(
            masked ? "Masked.comp" : "Flat.comp"))
        #expect(snapshot.masks.count == (masked ? 1 : 0))
        #expect(snapshot.manifest.version == 8 && snapshot.manifest.layers.count == 2)
        #expect(snapshot.manifest.layers.last?.name == "Top")
        let rendered = try await ImageExporter.shared.render(snapshot).image
        #expect(rendered.width == reference.width && rendered.height == reference.height)
        let expected = try pixels(reference)
        let actual = try pixels(rendered)
        try #require(actual.count == expected.count)
        let differences = zip(actual, expected).map { abs(Int($0) - Int($1)) }
        let maximum = differences.max() ?? 0
        let changedChannels = differences.filter { $0 != 0 }.count
        if let output {
            let directory = URL(fileURLWithPath: output)
            try #require(!FileManager.default.fileExists(atPath: directory.path), "Use a fresh output directory")
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let png = try #require(NSBitmapImageRep(cgImage: rendered).representation(using: .png, properties: [:]))
            try png.write(to: directory.appendingPathComponent("mac-render.png"))
            let report: [String: Any] = ["maximumChannelError": maximum, "changedChannels": changedChannels,
                                         "comparedChannels": actual.count, "exact": maximum == 0]
            try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
                .write(to: directory.appendingPathComponent("comparison.json"))
        }
        #expect(maximum == 0, "Flat Normal production/Mac mismatch: max \(maximum), \(changedChannels) channels")
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_WORKFLOW_DIR"] != nil,
                   "Run Compositor.Workflow.Checks and set WINDOWS_PRODUCTION_WORKFLOW_DIR to its output."),
          arguments: ["Image", "Edited", "Oriented"])
    func productionV8PixelsAndHistorySurviveMacRoundTrip(_ name: String) async throws {
        let root = URL(fileURLWithPath: try #require(
            ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_WORKFLOW_DIR"]))
        let package = root.appendingPathComponent(name + ".comp")
        let referenceName = name == "Image" ? "memory-export.png" :
            (name == "Edited" ? "export.png" : "oriented-export.png")
        let reference = try #require(NSBitmapImageRep(
            data: Data(contentsOf: root.appendingPathComponent(referenceName)))?.cgImage)
        let expectedPixels = try pixels(reference)
        let snapshot = try await ProjectStore.shared.load(from: package)
        #expect(snapshot.manifest.version == 8 && snapshot.manifest.resolution == 72)
        #expect(snapshot.manifest.width == reference.width && snapshot.manifest.height == reference.height)
        #expect(snapshot.manifest.layers.count == 1 && snapshot.images.count == 1 && snapshot.masks.isEmpty)
        let layer = try #require(snapshot.manifest.layers.first)
        #expect(snapshot.manifest.activeLayerID == layer.id && layer.isVisible)
        #expect(layer.name == (name == "Edited" ? "Edited image" : "Image"))
        #expect(layer.transform.origin == .zero)
        #expect(layer.transform.size == CGSize(width: reference.width, height: reference.height))
        #expect(try pixels(await ImageExporter.shared.render(snapshot).image) == expectedPixels,
                "Actual Mac reader/render must preserve the production export")

        let session = EditorSession()
        session.installProject(snapshot, from: package)
        #expect(!session.isModified && !session.canUndo)
        session.renameLayer(layer.id, to: "Mac 编辑 " + name)
        #expect(session.isModified && session.activeLayer?.name == "Mac 编辑 " + name)
        session.undo()
        #expect(!session.isModified && session.activeLayer?.name == layer.name)
        session.redo()
        #expect(session.isModified && session.activeLayer?.name == "Mac 编辑 " + name)
        let edited = try #require(session.projectSnapshot())
        #expect(try pixels(await ImageExporter.shared.render(edited).image) == expectedPixels)

        let retainedOutput = ProcessInfo.processInfo.environment["MAC_PRODUCTION_ROUNDTRIP_DIR"]
        let saved: URL
        if let retainedOutput {
            let directory = URL(fileURLWithPath: retainedOutput)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            saved = directory.appendingPathComponent(name + ".comp")
            try #require(!FileManager.default.fileExists(atPath: saved.path), "Use a fresh output directory")
        } else {
            saved = FileManager.default.temporaryDirectory
                .appendingPathComponent("Compositor-Production-\(name)-\(UUID()).comp")
        }
        defer { if retainedOutput == nil { try? FileManager.default.removeItem(at: saved) } }
        try await ProjectStore.shared.save(edited, to: saved)
        let reopened = try await ProjectStore.shared.load(from: saved)
        #expect(reopened.manifest.documentID == snapshot.manifest.documentID)
        #expect(reopened.manifest.layers.first?.id == layer.id)
        #expect(reopened.manifest.layers.first?.name == "Mac 编辑 " + name)
        #expect(try pixels(await ImageExporter.shared.render(reopened).image) == expectedPixels,
                "Mac editing, save and reopen must retain the imported pixels")
    }
}
