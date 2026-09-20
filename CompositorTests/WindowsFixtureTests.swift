import AppKit
import Testing
@testable import Compositor

/// Nonempty, version-specific schema reconstructions, not historical application exports.
@MainActor
struct WindowsFixtureTests {
    private func id(_ number: Int) -> UUID {
        UUID(uuidString: String(format: "00000000-0000-4000-8000-%012d", number))!
    }

    private func asset(mask: Bool = false) throws -> ImportedImage {
        let context = try BrushRaster.context(width: 64, height: 48, mask: mask)
        if mask {
            for x in 0..<64 {
                context.setFillColor(gray: CGFloat(x) / 63, alpha: 1)
                context.fill(CGRect(x: x, y: 0, width: 1, height: 48))
            }
        } else {
            context.setFillColor(CGColor(srgbRed: 0.2, green: 0.6, blue: 0.8, alpha: 0.75))
            context.fill(CGRect(x: 0, y: 0, width: 64, height: 48))
            context.setFillColor(CGColor(srgbRed: 1, green: 0.1, blue: 0.3, alpha: 0.5))
            context.fill(CGRect(x: 8, y: 9, width: 27, height: 21))
        }
        let image = try #require(context.makeImage())
        return ImportedImage(image: image, thumbnail: image, name: "Generated geometric pixels")
    }

    private func snapshot(version: Int, blend: LayerBlendMode = .multiply) throws -> ProjectSnapshot {
        let image = try asset(), mask = try asset(mask: true)
        let transform = LayerTransform(origin: .zero, size: CGSize(width: 64, height: 48))
        var base = ProjectLayerRecord(id: id(1), name: "Base", isVisible: true,
            transform: transform, imageFile: "\(id(1)).png")
        var layers: [ProjectLayerRecord] = []
        var images = [id(1): image], masks: [UUID: ImportedImage] = [:]
        if version >= 2 {
            var group = ProjectLayerRecord(id: id(2), name: "Pass-through group", isVisible: true,
                transform: transform, imageFile: nil, isGroup: true)
            base.parentID = group.id
            if version >= 6 {
                group.maskFile = "\(group.id).mask.png"
                group.maskEnabled = true
                masks[group.id] = mask
            }
            layers.append(group)
        }
        if version >= 4 {
            base.maskFile = "\(base.id).mask.png"
            base.maskEnabled = true
            masks[base.id] = mask
        }
        layers.append(base)
        if version >= 3 {
            var top = ProjectLayerRecord(id: id(3), name: "Soft overlay", isVisible: true,
                transform: LayerTransform(origin: CGPoint(x: 10, y: 6), size: CGSize(width: 44, height: 32)),
                imageFile: "\(id(3)).png", parentID: id(2), opacity: 0.55, blendMode: blend)
            if version >= 5 { top.maskSourceID = base.id }
            layers.append(top)
            images[top.id] = image
        }
        if version >= 7 {
            layers.append(ProjectLayerRecord(id: id(4), name: "Clipped saturation", isVisible: true,
                transform: transform, imageFile: nil, parentID: id(2), opacity: 0.6,
                maskSourceID: base.id, adjustment: LayerAdjustment(kind: .hsv, saturation: -65)))
        }
        if version >= 8 {
            var style = LayerTextStyle.initial
            style.content = "跨平台 Ab\n🙂 e\u{301}"
            style.fontSizePoints = 10
            style.layout = .box(width: 56)
            style.red = 0.9
            let image = try TextLayoutSession(style: style, resolution: 72).render().image
            let text = ProjectLayerRecord(id: id(5), name: "Editable text", isVisible: true,
                transform: LayerTransform(origin: CGPoint(x: 4, y: 4), size: CGSize(width: image.width, height: image.height)),
                imageFile: "\(id(5)).png", text: style)
            layers.append(text)
            images[text.id] = ImportedImage(image: image, thumbnail: image, name: text.name)
        }
        return ProjectSnapshot(manifest: ProjectManifest(version: version, resolution: version == 1 ? nil : 72,
            documentID: id(100 + version), width: 64, height: 48, activeLayerID: base.id, layers: layers),
            images: images, masks: masks)
    }

    private func pixels(_ image: CGImage) throws -> Data {
        let context = try BrushRaster.context(width: image.width, height: image.height, mask: false)
        BrushRaster.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height), mask: false, context: context)
        return Data(bytes: try #require(context.data), count: context.bytesPerRow * context.height)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["AVALONIA_ROUNDTRIP_DIR"] != nil,
                   "Run the Avalonia specimen probe, then set AVALONIA_ROUNDTRIP_DIR to its output."))
    func avaloniaRenamedCopiesReopenWithOriginalPixels() async throws {
        let path = try #require(ProcessInfo.processInfo.environment["AVALONIA_ROUNDTRIP_DIR"])
        try await renamedCopiesReopenWithOriginalPixels(path: path)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["QT_ROUNDTRIP_DIR"] != nil,
                   "Run the Qt specimen probe, then set QT_ROUNDTRIP_DIR to its output."))
    func qtRenamedCopiesReopenWithOriginalPixels() async throws {
        let path = try #require(ProcessInfo.processInfo.environment["QT_ROUNDTRIP_DIR"])
        try await renamedCopiesReopenWithOriginalPixels(path: path)
    }

    private func renamedCopiesReopenWithOriginalPixels(path: String) async throws {
        let output = URL(fileURLWithPath: path)
        let fixtures = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/windows/fixtures")
        let names = (1...7).map { String(format: "F%02d", $0) } + (1...13).map { String(format: "B%02d", $0) }
        let packages = try FileManager.default.contentsOfDirectory(at: output, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "comp" }
        #expect(Set(packages.map { $0.deletingPathExtension().lastPathComponent }) == Set(names))
        let encoder = JSONEncoder()
        for name in names {
            let source = fixtures.appendingPathComponent(name + ".comp")
            let saved = output.appendingPathComponent(name + ".comp")
            let original = try await ProjectStore.shared.load(from: source)
            let renamed = try await ProjectStore.shared.load(from: saved)
            var expected = try #require(JSONSerialization.jsonObject(with: encoder.encode(original.manifest)) as? [String: Any])
            expected["version"] = 8
            expected["resolution"] = original.manifest.resolution ?? 72
            var layers = try #require(expected["layers"] as? [[String: Any]])
            let active = try #require(original.manifest.activeLayerID)
            let index = try #require(original.manifest.layers.firstIndex { $0.id == active })
            layers[index]["name"] = "跨平台 renamed " + name
            expected["layers"] = layers
            let actual = try JSONSerialization.jsonObject(with: encoder.encode(renamed.manifest))
            #expect(try JSONSerialization.data(withJSONObject: expected, options: .sortedKeys)
                == JSONSerialization.data(withJSONObject: actual, options: .sortedKeys), "Manifest: \(name)")
            for record in original.manifest.layers {
                for file in [record.imageFile, record.maskFile].compactMap({ $0 }) {
                    #expect(try Data(contentsOf: source.appendingPathComponent("images/" + file))
                        == Data(contentsOf: saved.appendingPathComponent("images/" + file)), "PNG/mask: \(name)")
                }
            }
            let before = try await ImageExporter.shared.render(original).image
            let after = try await ImageExporter.shared.render(renamed).image
            #expect(try pixels(before) == pixels(after), "Mac reopens cross-platform output: \(name)")
        }
    }

    @Test func frozenProjectsMatchTheirMacReferencePixels() async throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/windows/fixtures")
        let packages = try FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "comp" }.sorted { $0.path < $1.path }
        #expect(packages.count == 21)
        for package in packages {
            let referenceURL = root.appendingPathComponent(package.deletingPathExtension().lastPathComponent + "-mac.png")
            let reference = try #require(NSBitmapImageRep(data: Data(contentsOf: referenceURL))?.cgImage)
            let snapshot = try await ProjectStore.shared.load(from: package)
            let rendered = try await ImageExporter.shared.render(snapshot).image
            #expect(try pixels(rendered) == pixels(reference), "Frozen reference: \(package.lastPathComponent)")
        }
    }

    @Test func versionedProjectsPreserveSchemaAssetsAndReferenceComposite() async throws {
        FontLibrary.shared.registerBundledAndImportedFonts()
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("Compositor-Windows-Fixtures-\(UUID())")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let cases = (1...8).map { ("F0\($0)", $0, LayerBlendMode.multiply) }
            + LayerBlendMode.allCases.enumerated().map { ("B\(String(format: "%02d", $0.offset + 1))", 3, $0.element) }
        for (name, version, mode) in cases {
            var original = try snapshot(version: version, blend: mode)
            // Blend references use a minimal two-layer setup, but declare the current format:
            // the four nonseparable modes were not in the original v3 documented enum.
            if name.hasPrefix("B") {
                var manifest = original.manifest
                manifest.version = 8
                original = ProjectSnapshot(manifest: manifest, images: original.images, masks: original.masks)
            }
            let package = root.appendingPathComponent("\(name).comp")
            try await ProjectStore.shared.save(original, to: package)
            let loaded = try await ProjectStore.shared.load(from: package)
            #expect(try encoder.encode(loaded.manifest) == encoder.encode(original.manifest))
            #expect(!loaded.images.isEmpty)
            for (id, asset) in original.images {
                #expect(try pixels(#require(loaded.images[id]?.image)) == pixels(asset.image))
            }
            for (id, asset) in original.masks {
                #expect(try pixels(#require(loaded.masks[id]?.image)) == pixels(asset.image))
            }
            let before = try await ImageExporter.shared.render(original).image
            let after = try await ImageExporter.shared.render(loaded).image
            #expect(try pixels(before) == pixels(after))
            try await ImageExporter.shared.pngData(loaded).write(to: root.appendingPathComponent("\(name)-mac.png"))

            // Old fixtures must actually omit fields unavailable in that schema.
            let json = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: package.appendingPathComponent("manifest.json"))) as? [String: Any])
            let records = try #require(json["layers"] as? [[String: Any]])
            var allowed: Set<String> = ["id", "name", "isVisible", "transform", "imageFile"]
            if version >= 2 { allowed.formUnion(["parentID", "isGroup"]) }
            if version >= 3 { allowed.formUnion(["opacity", "blendMode"]) }
            if version >= 4 { allowed.formUnion(["maskFile", "maskEnabled"]) }
            if version >= 5 { allowed.insert("maskSourceID") }
            if version >= 7 { allowed.insert("adjustment") }
            if version >= 8 { allowed.insert("text") }
            for record in records { #expect(Set(record.keys).isSubset(of: allowed)) }

            let reopened = EditorSession()
            reopened.installProject(loaded, from: package)
            let upgraded = try #require(reopened.projectSnapshot())
            #expect(upgraded.manifest.version == 8)
            #expect(upgraded.manifest.layers.map(\.id) == original.manifest.layers.map(\.id))
            let upgradeURL = root.appendingPathComponent("\(name)-resaved-v8.comp")
            try await ProjectStore.shared.save(upgraded, to: upgradeURL)
            let reread = try await ProjectStore.shared.load(from: upgradeURL)
            #expect(try encoder.encode(reread.manifest) == encoder.encode(upgraded.manifest))
            #expect(try pixels(await ImageExporter.shared.render(reread).image) == pixels(before))
        }
        print("WINDOWS_FIXTURE_OUTPUT=\(root.path)")
    }
}
