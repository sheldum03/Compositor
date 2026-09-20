// Mac-only fixture generator/reference decoder; not part of Windows application IO.
import Foundation
import CoreGraphics
import CoreImage
import ImageIO
import UniformTypeIdentifiers

func need<T>(_ value: T?) throws -> T {
    guard let value else { throw NSError(domain: "HEIC fixture", code: 1) }
    return value
}
let root = URL(fileURLWithPath: CommandLine.arguments[1])
guard !FileManager.default.fileExists(atPath: root.path) else { fatalError("Output must be new") }
try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
let width = 96, height = 64
let space = try need(CGColorSpace(name: CGColorSpace.sRGB))
let context = CIContext(options: [.cacheIntermediates: false])
var records: [[String: Any]] = []
for orientation in 1...8 {
    for alpha in [false, true] {
        var bytes = [UInt8](repeating: 0, count: width * height * 4)
        for y in 0..<height { for x in 0..<width {
            let i = (y * width + x) * 4
            let a: UInt8 = alpha ? (x < 24 ? 0 : x < 48 ? 128 : 255) : 255
            let rgb: [UInt8] = y < 32 ? (x < 48 ? [230, 40, 20] : [20, 180, 70]) : (x < 48 ? [30, 60, 220] : [220, 180, 30])
            for c in 0..<3 { bytes[i + c] = UInt8((Int(rgb[c]) * Int(a) + 127) / 255) }
            bytes[i + 3] = a
        }}
        let provider = try need(CGDataProvider(data: Data(bytes) as CFData))
        let image = try need(CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: width * 4, space: space,
            bitmapInfo: CGBitmapInfo(rawValue: CGBitmapInfo.byteOrder32Big.rawValue | CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent))
        let name = "orientation-\(orientation)-\(alpha ? "alpha" : "opaque")"
        let url = root.appendingPathComponent(name + ".heic")
        let destination = try need(CGImageDestinationCreateWithURL(url as CFURL, UTType.heic.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: 1,
            kCGImagePropertyOrientation: orientation] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { fatalError("HEIC encoding failed") }
        let source = try need(CGImageSourceCreateWithURL(url as CFURL, nil))
        let properties = try need(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
        let decoded = try need(CGImageSourceCreateImageAtIndex(source, 0, nil))
        let exifOrientation = (properties[kCGImagePropertyOrientation] as? Int32) ?? 1
        let oriented = CIImage(cgImage: decoded).oriented(forExifOrientation: exifOrientation)
        let reference = try need(context.createCGImage(oriented, from: oriented.extent, format: .RGBA8, colorSpace: space))
        let png = try need(CGImageDestinationCreateWithURL(root.appendingPathComponent(name + "-mac.png") as CFURL, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(png, reference, nil)
        guard CGImageDestinationFinalize(png) else { fatalError("Reference PNG") }
        records.append(["name": name, "sourceWidth": width, "sourceHeight": height, "requestedOrientation": orientation,
            "macOrientation": exifOrientation, "requestedAlpha": alpha, "macHasAlpha": properties[kCGImagePropertyHasAlpha] as? Bool ?? false,
            "referenceWidth": reference.width, "referenceHeight": reference.height])
    }
}
try JSONSerialization.data(withJSONObject: records, options: [.prettyPrinted, .sortedKeys]).write(to: root.appendingPathComponent("cases.json"))
print("Generated sixteen HEIC fixtures and actual ImageIO/CI oriented sRGB references")
