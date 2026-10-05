import AppKit
import Testing
@testable import Compositor

@MainActor
struct WindowsProductionBlendTests {
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

    @Test(.enabled(if: ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_BLEND_DIR"] != nil,
                   "Set WINDOWS_PRODUCTION_BLEND_DIR to formal 13-mode production output."),
          arguments: Array(1...13))
    func allProductionBlendModesMatchMac(_ index: Int) async throws {
        let root = URL(fileURLWithPath: try #require(ProcessInfo.processInfo.environment["WINDOWS_PRODUCTION_BLEND_DIR"]))
        let output = URL(fileURLWithPath: try #require(ProcessInfo.processInfo.environment["MAC_PRODUCTION_BLEND_OUTPUT_DIR"]))
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        let name = String(format: "B%02d-flat", index)
        let snapshot = try await ProjectStore.shared.load(from: root.appendingPathComponent(name + ".comp"))
        let top = try #require(snapshot.manifest.layers.last)
        let mode = LayerBlendMode.allCases[index - 1]
        #expect(snapshot.manifest.layers.count == 2 && snapshot.manifest.width == 300 && snapshot.manifest.height == 257)
        #expect((top.blendMode ?? .normal) == mode && top.opacity == 0.55 && top.isVisible)
        #expect(snapshot.manifest.layers.allSatisfy { $0.parentID == nil && $0.isGroup != true && $0.maskFile == nil })
        let mac = try await ImageExporter.shared.render(snapshot).image
        let production = try #require(NSBitmapImageRep(data: Data(contentsOf: root.appendingPathComponent(name + ".png")))?.cgImage)
        let macBytes = try pixels(mac), productionBytes = try pixels(production)
        for (position, layer) in snapshot.manifest.layers.enumerated() {
            let source = try #require(snapshot.images[layer.id]?.image)
            try pixels(source).write(to: output.appendingPathComponent(name + "-source-\(position).rgba"))
        }
        var histogram = [Int: Int](), changedPixels = 0, alphaChangedPixels = 0, maximum = 0, sum = 0
        var diff = Data(count: macBytes.count)
        for pixel in 0..<(macBytes.count / 4) {
            var changed = false
            for channel in 0..<4 {
                let offset = pixel * 4 + channel
                let delta = abs(Int(macBytes[offset]) - Int(productionBytes[offset]))
                histogram[delta, default: 0] += 1
                diff[offset] = UInt8(delta)
                maximum = max(maximum, delta); sum += delta
                changed = changed || delta != 0
                if channel == 3 && delta != 0 { alphaChangedPixels += 1 }
            }
            if changed { changedPixels += 1 }
        }
        try #require(NSBitmapImageRep(cgImage: mac).representation(using: .png, properties: [:]))
            .write(to: output.appendingPathComponent(name + "-mac.png"))
        try diff.write(to: output.appendingPathComponent(name + "-absolute-diff.rgba"))
        try macBytes.write(to: output.appendingPathComponent(name + "-mac.rgba"))
        try productionBytes.write(to: output.appendingPathComponent(name + "-production-cg.rgba"))
        let report: [String: Any] = [
            "mode": mode.rawValue, "index": index, "channels": macBytes.count,
            "changedPixels": changedPixels, "alphaChangedPixels": alphaChangedPixels,
            "maximumChannelDifference": maximum, "meanChannelDifference": Double(sum) / Double(macBytes.count),
            "histogram": Dictionary(uniqueKeysWithValues: histogram.map { (String($0.key), $0.value) }),
            "exact": maximum == 0, "threshold": 0
        ]
        try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
            .write(to: output.appendingPathComponent(name + "-comparison.json"))
        #expect(maximum == 0, "\(mode.rawValue): exact Mac equality required, maximum \(maximum), changed pixels \(changedPixels)")
        let saved = output.appendingPathComponent(name + "-mac-resaved.comp")
        try #require(!FileManager.default.fileExists(atPath: saved.path), "Use a fresh Mac output directory")
        try await ProjectStore.shared.save(snapshot, to: saved)
        let reopened = try await ProjectStore.shared.load(from: saved)
        #expect(reopened.manifest.documentID == snapshot.manifest.documentID)
        #expect(reopened.manifest.layers.map(\.id) == snapshot.manifest.layers.map(\.id))
        #expect(reopened.manifest.layers.last?.opacity == top.opacity && (reopened.manifest.layers.last?.blendMode ?? .normal) == mode)
        #expect(try pixels(await ImageExporter.shared.render(reopened).image).elementsEqual(macBytes))
    }
}
