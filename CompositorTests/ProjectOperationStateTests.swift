import AppKit
import Testing
@testable import Compositor

@MainActor
struct ProjectOperationStateTests {
    private func workspace() throws -> (ProjectWorkspace, ProjectTab, ProjectTab) {
        let workspace = ProjectWorkspace(), first = workspace.current
        first.session.createDocument(width: 64, height: 64)
        let context = try BrushRaster.context(width: 32, height: 32, mask: false)
        context.setFillColor(CGColor(srgbRed: 0.2, green: 0.4, blue: 0.6, alpha: 0.8))
        context.fill(CGRect(x: 0, y: 0, width: 32, height: 32))
        let image = try #require(context.makeImage())
        first.session.insert(ImportedImage(image: image, thumbnail: image, name: "State fixture"))
        let second = workspace.addTab(reuseEmpty: false)
        workspace.select(first.id)
        return (workspace, first, second)
    }
    private func destination(_ session: EditorSession) throws -> URL {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("Project-State-\(UUID())")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: false)
        session.projectURL = root.appendingPathComponent("state.comp")
        return root
    }
    private func manifest(_ snapshot: ProjectSnapshot) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(snapshot.manifest)
    }
    private func pixels(_ image: CGImage) throws -> Data {
        let context = try BrushRaster.context(width: image.width, height: image.height, mask: false)
        BrushRaster.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height), mask: false, context: context)
        let pointer = try #require(context.data)
        var result = Data()
        for row in 0..<image.height {
            result.append(Data(bytes: pointer.advanced(by: row * context.bytesPerRow), count: image.width * 4))
        }
        return result
    }

    enum BlockedState: CaseIterable { case brush, text, levels, busy, importing }
    @Test(arguments: BlockedState.allCases)
    func rejectedSaveAndTabSwitchPreserveDocumentAndHistory(_ state: BlockedState) async throws {
        let (workspace, tab, other) = try workspace(), session = tab.session
        let root = try destination(session)
        defer { try? FileManager.default.removeItem(at: root) }
        switch state {
        case .brush:
            session.selectTool(.brush)
            session.beginBrush(at: CGPoint(x: 30, y: 30))
            _ = try #require(session.brushStroke)
        case .text:
            session.selectTool(.text)
            session.beginText(at: CGPoint(x: 5, y: 5)); session.finishTextPlacement()
            session.updateTextStyle { $0.content = "Pending text" }
            _ = try #require(session.textDraft)
        case .levels:
            session.beginLevels()
            _ = try #require(session.levels)
        case .busy: session.isProjectBusy = true
        case .importing: session.isImporting = true
        }
        defer {
            session.cancelBrush(); session.cancelText(); session.cancelLevels()
            session.isProjectBusy = false; session.isImporting = false
        }
        let before = session.document, count = session.history.undoCount
        #expect(!tab.controller.canStart && !workspace.canSwitch)
        #expect(await tab.controller.save() == false)
        workspace.select(other.id)
        session.undo()
        #expect(workspace.current === tab)
        #expect(session.document == before && session.history.undoCount == count)
        #expect(!FileManager.default.fileExists(atPath: try #require(session.projectURL).path))
        switch state {
        case .brush: #expect(session.brushStroke != nil)
        case .text: #expect(session.textDraft != nil)
        case .levels: #expect(session.levels != nil)
        case .busy: #expect(session.isProjectBusy)
        case .importing: #expect(session.isImporting)
        }
    }

    @Test(arguments: [false, true])
    func saveResolvesTransformOrCropBeforeSnapshot(transform: Bool) async throws {
        let (_, tab, _) = try workspace(), session = tab.session
        let root = try destination(session)
        defer { try? FileManager.default.removeItem(at: root) }
        let original = try #require(session.activeLayer?.transform), count = session.history.undoCount
        var expected = original
        if transform {
            session.beginTransform()
            _ = try #require(session.transformEdit)
            expected.origin.x += 7
            session.previewTransform(expected)
        } else {
            session.selectTool(.crop)
            session.cropRect = CGRect(x: 8, y: 8, width: 24, height: 24)
        }
        #expect(await tab.controller.save())
        #expect(session.transformEdit == nil && session.cropRect == nil && !session.isProjectBusy)
        #expect(session.activeLayer?.transform == expected)
        #expect(session.history.undoCount == count + (transform ? 1 : 0))
        #expect(!session.isModified)
        let saved = try await ProjectStore.shared.load(from: #require(session.projectURL))
        #expect(saved.manifest.width == 64 && saved.manifest.height == 64)
        #expect(saved.manifest.layers.first?.transform == expected)
        if transform {
            session.undo()
            #expect(session.activeLayer?.transform == original && session.isModified)
            session.redo()
            #expect(session.activeLayer?.transform == expected && !session.isModified)
        }
    }

    enum PendingState: CaseIterable { case gradient, hue, filter, pixelMove }
    // Characterize the current Mac entry-point difference; this is not a Windows policy decision.
    @Test(arguments: PendingState.allCases)
    func macSaveWritesCommittedDocumentWhileOtherPreviewsRemainPending(_ state: PendingState) async throws {
        let (workspace, tab, other) = try workspace(), session = tab.session
        let root = try destination(session)
        defer { try? FileManager.default.removeItem(at: root) }
        switch state {
        case .gradient:
            session.selectTool(.gradient)
            session.beginGradient(at: CGPoint(x: 20, y: 30))
            session.moveGradient(end: CGPoint(x: 40, y: 30)); session.endGradientDrag()
            _ = try #require(session.gradientEdit)
        case .hue:
            session.beginHueSaturation()
            _ = try #require(session.hueSaturation)
            session.updateHueSaturation(HueSaturationSettings(hue: 90, saturation: 30), preview: true)
        case .filter:
            session.beginFilter(.addNoise)
            _ = try #require(session.filterEdit)
        case .pixelMove:
            session.applySelection(CGPath(rect: CGRect(x: 20, y: 20, width: 8, height: 8), transform: nil), mode: .replace, name: "Select")
            #expect(session.beginPixelMove())
            session.movePixels(by: CGSize(width: 5, height: 0))
            _ = try #require(session.pixelMove)
        }
        defer {
            session.cancelGradient(); session.cancelHueSaturation(); session.cancelFilter(); session.cancelPixelMove()
        }
        let before = try #require(session.projectSnapshot()), document = session.document
        let count = session.history.undoCount
        #expect(tab.controller.canStart && !workspace.canSwitch)
        workspace.select(other.id)
        #expect(workspace.current === tab)
        #expect(await tab.controller.save())
        let saved = try await ProjectStore.shared.load(from: #require(session.projectURL))
        #expect(try manifest(saved) == manifest(before))
        for (id, asset) in before.images {
            let loaded = try #require(saved.images[id]?.image)
            #expect(loaded.width == asset.image.width && loaded.height == asset.image.height)
            #expect(try pixels(loaded) == pixels(asset.image))
        }
        #expect(session.document == document && session.history.undoCount == count)
        #expect(!session.isModified && !session.isProjectBusy)
        switch state {
        case .gradient: #expect(session.gradientEdit != nil)
        case .hue: #expect(session.hueSaturation != nil)
        case .filter: #expect(session.filterEdit != nil)
        case .pixelMove: #expect(session.pixelMove != nil)
        }
        if state == .gradient {
            await session.commitGradient()
            #expect(session.gradientEdit == nil && session.history.undoCount == count + 1)
            #expect(session.isModified)
            session.undo()
            #expect(session.document == document && !session.isModified)
        }
    }

    @Test func tabSwitchCommitsTransformAndKeepsHistoryInOriginalTab() throws {
        let (workspace, tab, other) = try workspace(), session = tab.session
        let original = try #require(session.activeLayer?.transform), count = session.history.undoCount
        session.beginTransform()
        var changed = original; changed.origin.y += 9
        session.previewTransform(changed)
        workspace.select(other.id)
        #expect(workspace.current === other && session.transformEdit == nil)
        #expect(session.activeLayer?.transform == changed && session.history.undoCount == count + 1)
        #expect(other.session.document == nil && other.session.history.undoCount == 0)
        workspace.select(tab.id)
        session.undo()
        #expect(session.activeLayer?.transform == original)
    }

    @Test(arguments: [false, true])
    func toolSwitchOnlyProceedsAfterTextCommits(oversized: Bool) throws {
        let (_, tab, _) = try workspace(), session = tab.session
        let original = session.document, count = session.history.undoCount
        session.selectTool(.text)
        session.textStyle.fontPostScriptName = "Helvetica"
        session.beginText(at: CGPoint(x: 5, y: 5))
        session.dragText(to: CGPoint(x: oversized ? 30_020 : 55, y: 30))
        session.finishTextPlacement()
        session.updateTextStyle { $0.content = "A" }
        _ = try #require(session.textDraft)
        session.selectTool(.hand)
        if oversized {
            #expect(session.tool == .text && session.textDraft != nil)
            #expect(session.document == original && session.history.undoCount == count)
            session.cancelText()
        } else {
            #expect(session.tool == .hand && session.textDraft == nil)
            #expect(session.activeLayer?.liveText?.style.content == "A")
            #expect(session.history.undoCount == count + 1)
            session.undo()
            #expect(session.document == original)
        }
    }

    @Test(arguments: [false, true])
    func nativeFocusLossCancelsBrushUnlessCommitIsBusy(busy: Bool) throws {
        let (_, tab, _) = try workspace(), session = tab.session
        let canvas = CanvasView(session: session)
        let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 200, height: 200),
                              styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = canvas
        #expect(window.makeFirstResponder(canvas))
        let original = session.document, count = session.history.undoCount
        session.selectTool(.brush); session.beginBrush(at: CGPoint(x: 30, y: 30))
        _ = try #require(session.brushStroke)
        session.isProjectBusy = busy
        #expect(window.makeFirstResponder(nil))
        #expect((session.brushStroke != nil) == busy)
        #expect(session.document == original && session.history.undoCount == count)
        session.isProjectBusy = false
        session.cancelBrush()
    }

    @Test(arguments: [LassoKind.freehand, .polygonal])
    func nativeFocusLossPreservesOnlyPolygonalLasso(_ kind: LassoKind) throws {
        let (_, tab, _) = try workspace(), session = tab.session
        let canvas = CanvasView(session: session)
        let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 200, height: 200),
                              styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = canvas
        #expect(window.makeFirstResponder(canvas))
        let original = session.document, count = session.history.undoCount
        session.selectTool(.lasso); session.lassoKind = kind
        session.beginLasso(at: CGPoint(x: 20, y: 20), mode: .replace)
        session.extendLasso(to: CGPoint(x: 40, y: 20))
        _ = try #require(session.lassoDraft)
        #expect(window.makeFirstResponder(nil))
        #expect((session.lassoDraft != nil) == (kind == .polygonal))
        #expect(session.document == original && session.history.undoCount == count)
        session.cancelLasso()
    }
}
