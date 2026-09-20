// Standalone numeric diagnostic, not a shipping brush asset or a test of the whole Mac application.
// Recreates the fixed specimen's private BrushStroke.tip construction with Core Graphics.
import Foundation
import CoreGraphics

guard CommandLine.arguments.count == 2 else { fatalError("Pass a new .gray8 output path") }
let destination = URL(fileURLWithPath: CommandLine.arguments[1])
guard !FileManager.default.fileExists(atPath: destination.path) else { fatalError("Output already exists") }
let size = 800
let context = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: size,
    space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)!
context.translateBy(x: 0, y: CGFloat(size))
context.scaleBy(x: 1, y: -1)
context.setFillColor(gray: 0, alpha: 1)
context.fill(CGRect(x: 0, y: 0, width: size, height: size))
let locations = (0...24).map { CGFloat($0) / 24 }
let levels: [CGFloat] = locations.flatMap {
    [max(0, (exp(-2.5 * $0 * $0) - exp(-2.5)) / (1 - exp(-2.5))), CGFloat(1)]
}
let gradient = CGGradient(colorSpace: CGColorSpaceCreateDeviceGray(), colorComponents: levels,
    locations: locations, count: locations.count)!
context.drawRadialGradient(gradient, startCenter: CGPoint(x: 400, y: 400), startRadius: 0,
    endCenter: CGPoint(x: 400, y: 400), endRadius: 400, options: [.drawsBeforeStartLocation])
try Data(bytes: context.data!, count: size * size).write(to: destination, options: .withoutOverwriting)
