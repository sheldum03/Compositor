import AppKit
import Testing
@testable import Compositor

@MainActor
struct WindowsBrushFixtureTests {
    private struct Input: Decodable {
        let width: Int
        let height: Int
        let diameter: CGFloat
        let hardness: CGFloat
        let opacity: CGFloat
        let color: [CGFloat]
        let strokes: [[[CGFloat]]]
    }
    private func pixels(_ image: CGImage) throws -> Data {
        let context = try BrushRaster.context(width: image.width, height: image.height, mask: false)
        BrushRaster.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height), mask: false, context: context)
        return Data(bytes: try #require(context.data), count: context.bytesPerRow * context.height)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["AVALONIA_BRUSH_DIR"] != nil,
                   "Run the Avalonia brush probe and set AVALONIA_BRUSH_DIR to its output."))
    func avaloniaBrushPackageReopensWithExportedPixels() async throws {
        let path = try #require(ProcessInfo.processInfo.environment["AVALONIA_BRUSH_DIR"])
        let root = URL(fileURLWithPath: path)
        let snapshot = try await ProjectStore.shared.load(from: root.appendingPathComponent("brush.comp"))
        #expect(snapshot.manifest.version == 8 && snapshot.manifest.width == 4000 && snapshot.manifest.height == 4000)
        #expect(snapshot.manifest.resolution == 72 && snapshot.manifest.layers.count == 1)
        let layer = try #require(snapshot.manifest.layers.first)
        #expect(layer.transform.origin == .zero && layer.transform.size == CGSize(width: 4000, height: 4000))
        let reference = try #require(NSBitmapImageRep(data: Data(contentsOf: root.appendingPathComponent("final.png")))?.cgImage)
        let rendered = try await ImageExporter.shared.render(snapshot).image
        #expect(try pixels(rendered) == pixels(reference), "Mac reads the C# brush output without changing pixels")
        let resaved = FileManager.default.temporaryDirectory.appendingPathComponent("Compositor-Avalonia-Brush-\(UUID()).comp")
        defer { try? FileManager.default.removeItem(at: resaved) }
        try await ProjectStore.shared.save(snapshot, to: resaved)
        let reopened = try await ProjectStore.shared.load(from: resaved)
        #expect(try pixels(await ImageExporter.shared.render(reopened).image) == pixels(reference))
    }

    @Test(arguments: [false, true])
    func fixedSoftStrokeSequenceCommitsLocallyAndPreservesHistory(useGPU: Bool) async throws {
        if useGPU { _ = try #require(MetalBrushCoverage.shared, "Metal reference requires actual Metal availability") }
        let inputURL = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/windows/fixtures/brush/soft-crossing-4k.json")
        let input = try JSONDecoder().decode(Input.self, from: Data(contentsOf: inputURL))
        #expect(input.strokes.count == 2 && input.strokes.allSatisfy { $0.count == 121 })
        let session = EditorSession()
        session.createDocument(width: input.width, height: input.height, emptyLayer: true)
        session.selectTool(.brush)
        let settings = BrushSettings(diameter: input.diameter, hardness: input.hardness,
            red: input.color[0], green: input.color[1], blue: input.color[2], opacity: input.opacity)
        let backend = useGPU ? "metal" : "cpu"
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("Compositor-Brush-\(backend)-\(UUID())")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        var first: ImportedImage?
        var firstSnapshot: ProjectSnapshot?
        var timings: [[String: Any]] = []
        let initialCount = session.history.undoCount
        for (index, path) in input.strokes.enumerated() {
            let stroke = try BrushStroke(layer: #require(session.activeLayer), mask: false, settings: settings,
                canvas: CGSize(width: input.width, height: input.height), useGPU: useGPU)
            // Inject only the chosen coverage backend; mouse-up uses the actual session commit path.
            session.brushStroke = stroke
            var updateMilliseconds: [Double] = []
            for coordinates in path {
                let start = CFAbsoluteTimeGetCurrent()
                try stroke.append(CGPoint(x: coordinates[0], y: coordinates[1]))
                updateMilliseconds.append((CFAbsoluteTimeGetCurrent() - start) * 1000)
            }
            let start = CFAbsoluteTimeGetCurrent()
            #expect(session.finishBrushImmediately())
            let commitMilliseconds = (CFAbsoluteTimeGetCurrent() - start) * 1000
            #expect(session.brushError == nil && session.brushStroke == nil && session.canPaint)
            #expect(session.history.undoCount == initialCount + index + 1)
            let asset = try #require(session.activeLayer?.asset)
            #expect(asset.raster?.hasMaterializedPixels == false)
            #expect(first?.raster?.hasMaterializedPixels != true)
            if index == 0 { first = asset; firstSnapshot = session.projectSnapshot() }
            timings.append(["stroke": index, "appendMilliseconds": updateMilliseconds, "commitMilliseconds": commitMilliseconds])
        }
        let firstAsset = try #require(first)
        let beforeSecond = try #require(firstSnapshot)
        let last = try #require(session.activeLayer?.asset)
        let finalSnapshot = try #require(session.projectSnapshot())
        let firstPixels = try pixels(await ImageExporter.shared.render(beforeSecond).image)
        let finalPixels = try pixels(await ImageExporter.shared.render(finalSnapshot).image)
        #expect(firstPixels != finalPixels)
        let referenceRoot = inputURL.deletingLastPathComponent()
        let firstReference = try #require(NSBitmapImageRep(data: Data(contentsOf:
            referenceRoot.appendingPathComponent("first-\(backend).png")))?.cgImage)
        let finalReference = try #require(NSBitmapImageRep(data: Data(contentsOf:
            referenceRoot.appendingPathComponent("final-\(backend).png")))?.cgImage)
        #expect(try pixels(firstReference) == firstPixels, "\(backend) first stroke reference")
        #expect(try pixels(finalReference) == finalPixels, "\(backend) next stroke reference")
        session.undo()
        #expect(session.activeLayer?.asset?.image === firstAsset.image)
        #expect(try pixels(await ImageExporter.shared.render(#require(session.projectSnapshot())).image) == firstPixels)
        session.redo()
        #expect(session.activeLayer?.asset?.image === last.image)
        #expect(try pixels(await ImageExporter.shared.render(#require(session.projectSnapshot())).image) == finalPixels)
        #expect(try pixels(await ImageExporter.shared.render(beforeSecond).image) == firstPixels)
        let package = root.appendingPathComponent("soft-crossing-4k-\(backend).comp")
        try await ProjectStore.shared.save(finalSnapshot, to: package)
        let reopened = try await ProjectStore.shared.load(from: package)
        #expect(try pixels(await ImageExporter.shared.render(reopened).image) == finalPixels)
        try await ImageExporter.shared.pngData(beforeSecond).write(to: root.appendingPathComponent("first-\(backend).png"))
        try await ImageExporter.shared.pngData(finalSnapshot).write(to: root.appendingPathComponent("final-\(backend).png"))
        let report: [String: Any] = ["backend": backend, "configuration": "Debug", "scope": "append and session mouse-up only; excludes display; not V-08 acceptance", "strokes": timings]
        try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
            .write(to: root.appendingPathComponent("timings-\(backend).json"))
        print("WINDOWS_BRUSH_FIXTURE_OUTPUT=\(root.path)")
    }
}
