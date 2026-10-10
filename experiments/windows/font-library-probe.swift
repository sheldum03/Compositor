import AppKit
import CoreText

// Same localization expression as CompositorApp; compile the real FontLibrary separately.
extension String {
    nonisolated var localized: String { String(localized: String.LocalizationValue(self)) }
}

@main
struct FontLibraryProbe {
    enum Failure: Error { case check(String) }
    static func check(_ condition: Bool, _ message: String) throws {
        if !condition { throw Failure.check(message) }
    }
    @MainActor static func main() {
        do { try run() }
        catch {
            FileHandle.standardError.write(Data("Font probe failed: \(error)\n".utf8))
            exit(1)
        }
    }
    @MainActor static func run() throws {
        try check(CommandLine.arguments.count == 4, "usage: font-probe import|restore fixtures library-directory")
        let phase = CommandLine.arguments[1]
        try check(["import", "restore"].contains(phase), "unknown phase")
        let fixtures = URL(fileURLWithPath: CommandLine.arguments[2], isDirectory: true)
        let directory = URL(fileURLWithPath: CommandLine.arguments[3], isDirectory: true)
        let expected: [(String, String, Double)] = [
            ("fixture.ttf", "CompositorFixtureTTF-Regular", 60),
            ("fixture.otf", "CompositorFixtureOTF-Regular", 60),
            ("two-faces.ttc", "CompositorFixtureCollection-Regular", 60),
            ("two-faces.ttc", "CompositorFixtureCollection-Bold", 80)
        ]
        for (_, name, _) in expected {
            try check(NSFont(name: name, size: 100) == nil, "\(name) unexpectedly available before registration")
        }
        let library = FontLibrary(fontsDirectory: directory, bundledURLs: [])
        if phase == "import" {
            try check(!FileManager.default.fileExists(atPath: directory.path), "import needs a fresh output directory")
            for file in ["fixture.ttf", "fixture.otf", "two-faces.ttc"] {
                try library.importFont(from: fixtures.appendingPathComponent(file))
            }
        } else {
            try check(FileManager.default.fileExists(atPath: directory.path), "missing persisted library")
            library.registerBundledAndImportedFonts()
        }
        try check(Set(try FileManager.default.contentsOfDirectory(atPath: directory.path)) ==
                  Set(["fixture.ttf", "fixture.otf", "two-faces.ttc"]), "persisted files changed")
        var faces: [[String: Any]] = []
        for (file, name, advance) in expected {
            guard let font = NSFont(name: name, size: 100) else { throw Failure.check("missing \(name)") }
            try check(library.availableFaces.contains { $0.postScriptName == name }, "face missing from picker")
            let actualAdvance = ("A" as NSString).size(withAttributes: [.font: font]).width
            try check(actualAdvance == advance, "wrong face metrics for \(name)")
            let ctFont = CTFontCreateWithName(name as CFString, 100, nil)
            guard let origin = CTFontCopyAttribute(ctFont, kCTFontURLAttribute) as? URL else {
                throw Failure.check("missing registered origin")
            }
            try check(origin.resolvingSymlinksInPath().standardizedFileURL ==
                      directory.appendingPathComponent(file).resolvingSymlinksInPath().standardizedFileURL,
                      "font came from another location")
            faces.append(["postScriptName": name, "glyphAAdvanceAt100pt": actualAdvance, "file": file])
        }
        let result: [String: Any] = ["phase": phase, "status": "passed", "pid": ProcessInfo.processInfo.processIdentifier,
                                    "initiallyUnavailable": true, "faces": faces]
        let data = try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys])
        print(String(decoding: data, as: UTF8.self))
    }
}
