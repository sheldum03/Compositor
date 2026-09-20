import AppKit
import Testing
@testable import Compositor

@MainActor
struct WindowsExtendedFixtureTests {
    private let names = ["F09-complex", "F12-missing-font"] + [72, 300].flatMap { dpi in
        ["point", "box"].flatMap { layout in
            ["left", "center", "right"].map { "F11-\(dpi)-\(layout)-\($0)" }
        }
    }
    private func id(_ number: Int) -> UUID {
        UUID(uuidString: String(format: "00000000-0000-4000-9000-%012d", number))!
    }
    private func pixels(_ image: CGImage) throws -> Data {
        let context = try BrushRaster.context(width: image.width, height: image.height, mask: false)
        BrushRaster.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height), mask: false, context: context)
        return Data(bytes: try #require(context.data), count: context.bytesPerRow * context.height)
    }
    private func complex() throws -> ProjectSnapshot {
        let canvas = LayerTransform(origin: .zero, size: CGSize(width: 128, height: 96))
        let group = ProjectLayerRecord(id: id(1), name: "Outer group", isVisible: true,
            transform: canvas, imageFile: nil, isGroup: true, maskFile: "\(id(1)).mask.png", maskEnabled: true)
        let nested = ProjectLayerRecord(id: id(2), name: "Inner group", isVisible: true,
            transform: canvas, imageFile: nil, parentID: group.id, isGroup: true)
        let hidden = ProjectLayerRecord(id: id(3), name: "Hidden group", isVisible: false,
            transform: canvas, imageFile: nil, isGroup: true)
        let blank = ProjectLayerRecord(id: id(4), name: "Empty layer", isVisible: true,
            transform: canvas, imageFile: nil, parentID: nested.id)
        let shape = LayerShapeStyle(kind: .rectangle, red: 0.9, green: 0.3, blue: 0.1, cornerRadius: 9)
        let shapeImage = try EditorSession.shapeImage(shape.kind, size: CGSize(width: 70, height: 44),
            color: shape.color, cornerRadius: shape.cornerRadius)
        let placed = LayerTransform(origin: CGPoint(x: 20, y: 22), size: CGSize(width: 70, height: 44),
            rotation: 23, flipX: true)
        let rectangle = ProjectLayerRecord(id: id(5), name: "Rounded shape with independent mask", isVisible: true,
            transform: placed, imageFile: "\(id(5)).png", parentID: nested.id, opacity: 0.72, blendMode: .screen,
            maskFile: "\(id(5)).mask.png", maskEnabled: true,
            maskPlacement: LayerTransform(origin: CGPoint(x: 12, y: 6), size: CGSize(width: 64, height: 80), rotation: -17),
            maskLinked: false, shape: shape)
        let ellipseStyle = LayerShapeStyle(kind: .ellipse, red: 0.1, green: 0.5, blue: 0.9, cornerRadius: 0)
        let ellipseImage = try EditorSession.shapeImage(.ellipse, size: CGSize(width: 56, height: 40), color: ellipseStyle.color)
        let ellipse = ProjectLayerRecord(id: id(6), name: "Ellipse with disabled mask", isVisible: true,
            transform: LayerTransform(origin: CGPoint(x: 50, y: 42), size: CGSize(width: 56, height: 40), flipY: true),
            imageFile: "\(id(6)).png", parentID: group.id, opacity: 0.6, blendMode: .multiply,
            maskFile: "\(id(6)).mask.png", maskEnabled: false, shape: ellipseStyle)
        let hiddenChild = ProjectLayerRecord(id: id(7), name: "Visible child of hidden parent", isVisible: true,
            transform: canvas, imageFile: "\(id(7)).png", parentID: hidden.id)
        let gray = try BrushRaster.context(width: 64, height: 48, mask: true)
        for y in 0..<48 {
            gray.setFillColor(gray: CGFloat(y) / 47, alpha: 1)
            gray.fill(CGRect(x: 0, y: y, width: 64, height: 1))
        }
        let mask = try LayerMask.asset(from: #require(gray.makeImage()))
        let shapeAsset = ImportedImage(image: shapeImage, thumbnail: shapeImage, name: rectangle.name)
        let ellipseAsset = ImportedImage(image: ellipseImage, thumbnail: ellipseImage, name: ellipse.name)
        return ProjectSnapshot(manifest: ProjectManifest(documentID: id(100), width: 128, height: 96,
            activeLayerID: rectangle.id, layers: [group, nested, blank, rectangle, ellipse, hidden, hiddenChild]),
            images: [rectangle.id: shapeAsset, ellipse.id: ellipseAsset, hiddenChild.id: shapeAsset],
            masks: [group.id: mask, rectangle.id: mask, ellipse.id: mask])
    }
    private func text(_ name: String) throws -> ProjectSnapshot {
        let dpi: Double = name.contains("300") ? 300 : 72
        var style = LayerTextStyle.initial
        style.content = "中文 English 🙂 e\u{301}\n第二行 — spacing and tracking"
        style.fontSizePoints = 18
        style.red = 0.15; style.green = 0.4; style.blue = 0.7; style.alpha = 0.8
        style.alignment = name.hasSuffix("center") ? .center : name.hasSuffix("right") ? .right : .left
        style.lineSpacingPoints = 3
        style.trackingPoints = 1.25
        style.layout = name.contains("point") ? .point : .box(width: 360)
        let image = try TextLayoutSession(style: style, resolution: dpi).render().image
        if name == "F12-missing-font" { style.fontPostScriptName = "Compositor-Fixture-Missing-Font" }
        var transform = LayerTransform(origin: .zero,
            size: CGSize(width: image.width, height: image.height), rotation: 13, flipX: true)
        let bounds = CGRect(x: 0, y: 0, width: 1, height: 1).applying(transform.unitToDocument)
        transform.origin = CGPoint(x: 60 - bounds.minX, y: 60 - bounds.minY)
        let layer = ProjectLayerRecord(id: id(10), name: name, isVisible: true,
            transform: transform,
            imageFile: "\(id(10)).png", opacity: 0.65, text: style)
        return ProjectSnapshot(manifest: ProjectManifest(resolution: dpi, documentID: id(101),
            width: Int(ceil(bounds.width + 120)), height: Int(ceil(bounds.height + 120)),
            activeLayerID: layer.id, layers: [layer]),
            images: [layer.id: ImportedImage(image: image, thumbnail: image, name: name)])
    }

    @Test func complexAndTextFixturesPreserveAllMetadataAndPixels() async throws {
        FontLibrary.shared.registerBundledAndImportedFonts()
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("Compositor-Extended-Fixtures-\(UUID())")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        for name in names {
            let original = try name == "F09-complex" ? complex() : text(name)
            let url = root.appendingPathComponent(name + ".comp")
            try await ProjectStore.shared.save(original, to: url)
            let loaded = try await ProjectStore.shared.load(from: url)
            #expect(try encoder.encode(loaded.manifest) == encoder.encode(original.manifest))
            for (id, asset) in original.images {
                #expect(try pixels(#require(loaded.images[id]?.image)) == pixels(asset.image))
            }
            for (id, asset) in original.masks {
                #expect(try pixels(#require(loaded.masks[id]?.image)) == pixels(asset.image))
            }
            let originalPixels = try pixels(await ImageExporter.shared.render(original).image)
            #expect(try pixels(await ImageExporter.shared.render(loaded).image) == originalPixels)
            let session = EditorSession()
            session.installProject(loaded, from: url)
            // Reopen must reconstruct live shape/text, not merely preserve opaque JSON.
            for record in original.manifest.layers {
                let layer = try #require(session.document?.layers.first { $0.id == record.id })
                #expect(layer.liveShape?.style == record.shape)
                #expect(layer.liveText?.style == record.text)
            }
            let resaved = try #require(session.projectSnapshot())
            let again = root.appendingPathComponent(name + "-resaved.comp")
            try await ProjectStore.shared.save(resaved, to: again)
            let reopened = try await ProjectStore.shared.load(from: again)
            #expect(try encoder.encode(reopened.manifest) == encoder.encode(resaved.manifest))
            #expect(try pixels(await ImageExporter.shared.render(reopened).image) == originalPixels)
            try await ImageExporter.shared.pngData(loaded).write(to: root.appendingPathComponent(name + "-mac.png"))
        }
        print("WINDOWS_EXTENDED_FIXTURE_OUTPUT=\(root.path)")
    }

    @Test func frozenComplexAndTextProjectsMatchReference() async throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/windows/fixtures/extended")
        for name in names {
            let reference = try #require(NSBitmapImageRep(data: Data(contentsOf: root.appendingPathComponent(name + "-mac.png")))?.cgImage)
            let snapshot = try await ProjectStore.shared.load(from: root.appendingPathComponent(name + ".comp"))
            #expect(try pixels(await ImageExporter.shared.render(snapshot).image) == pixels(reference), "Reference: \(name)")
        }
    }
}
