import AppKit

final class DisplayLabel: NSTextField {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
final class DisplayEffect: NSVisualEffectView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
// Match QuotaLabel.cs in 1.1.1: 190x96, left inset 24, right edge 166,
// title at 12, bold rows at 34/54, status at 76. macOS uses logical points.
final class QuotaTextView: NSView {
    var source = PetRGB.fallback { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    var content: (() -> (String, String))?
    override func draw(_ dirtyRect: NSRect) {
        guard let (rows, status) = content?() else { return }
        let ink = source.text.color, muted = source.secondary.color
        func text(_ value: String, rect: NSRect, font: NSFont, color: NSColor, right: Bool = false) {
            let paragraph = NSMutableParagraphStyle()
            paragraph.alignment = right ? .right : .left
            paragraph.lineBreakMode = .byTruncatingTail
            (value as NSString).draw(in: rect, withAttributes: [.font: font, .foregroundColor: color, .paragraphStyle: paragraph])
        }
        text("Quota remaining", rect: NSRect(x: 24, y: 12, width: 142, height: 18), font: .systemFont(ofSize: 13), color: ink)
        let amount = NSFont.systemFont(ofSize: 13, weight: .bold)
        for (index, row) in rows.components(separatedBy: "\n").prefix(2).enumerated() {
            let pair = row.components(separatedBy: "\t")
            let value = pair.count > 1 ? pair[1] : ""
            let width = (value as NSString).size(withAttributes: [.font: amount]).width
            let y = CGFloat(index == 0 ? 34 : 54)
            text(pair[0], rect: NSRect(x: 24, y: y, width: 142 - (value.isEmpty ? 0 : width + 12), height: 18), font: amount, color: ink)
            text(value, rect: NSRect(x: 24, y: y, width: 142, height: 18), font: amount, color: ink, right: true)
        }
        text(status, rect: NSRect(x: 24, y: 76, width: 142, height: 16), font: .systemFont(ofSize: 11), color: muted)
    }
}

final class BubblePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}
final class BubbleView: NSView {
    var onRefresh: (() -> Void)?
    var onPlacement: ((NSPoint) -> Void)?
    private var start: NSPoint?
    private var windowStart = NSPoint.zero
    private var dragged = false
    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard NSBezierPath(roundedRect: bounds, xRadius: 32, yRadius: 32).contains(local) else { return nil }
        return super.hitTest(point)
    }
    override func mouseDown(with event: NSEvent) {
        start = NSEvent.mouseLocation; windowStart = window?.frame.origin ?? .zero; dragged = false
    }
    override func mouseDragged(with event: NSEvent) {
        guard let start else { return }
        let current = NSEvent.mouseLocation
        if hypot(current.x - start.x, current.y - start.y) >= 4 { dragged = true }
        if dragged { window?.setFrameOrigin(NSPoint(x: windowStart.x + current.x - start.x, y: windowStart.y + current.y - start.y)) }
    }
    override func mouseUp(with event: NSEvent) {
        guard start != nil else { return }
        start = nil
        if dragged { onPlacement?(NSEvent.mouseLocation) }
        else if NSBezierPath(roundedRect: bounds, xRadius: 32, yRadius: 32).contains(convert(event.locationInWindow, from: nil)) { onRefresh?() }
    }
}
final class Host: NSObject {
    let app = NSApplication.shared
    let output: URL
    let preferencesURL: URL
    var preferences: Preferences
    let panel: BubblePanel
    let view: BubbleView
    let effect: NSVisualEffectView
    let tint = TintView()
    let palette = PetPaletteSource()
    var petColor = PetRGB.fallback
    var demoImages: [URL] = []
    let demoColors = [PetRGB(r: 255, g: 153, b: 0), PetRGB(r: 38, g: 101, b: 190), PetRGB(r: 147, g: 76, b: 182), PetRGB(r: 235, g: 235, b: 235)]
    var selectedImage: URL?
    var imageStamp = ""
    var paletteTimer: Timer?
    let textView = QuotaTextView()
    let quota = DisplayLabel(labelWithString: "Reading quota...")
    let caption = DisplayLabel(labelWithString: "")
    let dismissPanel: BubblePanel
    let pet: NSWindow
    let status: NSStatusItem
    var backdrop: NSWindow?
    var opacityItems: [NSMenuItem] = []
    var materialItems: [NSMenuItem] = []
    var hidden = false, busy = false
    var last: Quota?
    var lastChecked: Date?
    var queryCount = 0
    var refreshTimer: Timer?
    var followTimer: Timer?
    let ci: Bool
    let cli: String?
    init(output: URL, ci: Bool, cli: String?) {
        self.output = output; self.ci = ci; self.cli = cli
        let data = ProcessInfo.processInfo.environment["PETFOLIO_DATA_DIR"].map { URL(fileURLWithPath: $0) }
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/PetFolioMacDev")
        preferencesURL = data.appendingPathComponent("appearance.json")
        preferences = Preferences.load(preferencesURL)
        let frame = NSRect(x: 0, y: 0, width: 190, height: 96)
        panel = BubblePanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        dismissPanel = BubblePanel(contentRect: NSRect(x: 0, y: 0, width: 20, height: 20), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        view = BubbleView(frame: frame)
        effect = DisplayEffect(frame: frame)
        pet = NSWindow(contentRect: NSRect(x: 400, y: 300, width: 80, height: 80), styleMask: [.borderless], backing: .buffered, defer: false)
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        panel.isReleasedWhenClosed = false; panel.isOpaque = false; panel.backgroundColor = .clear
        panel.hasShadow = true; panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false; panel.contentView = view
        effect.material = .popover; effect.blendingMode = .behindWindow; effect.state = .active
        effect.wantsLayer = true
        let mask = NSImage(size: frame.size, flipped: false) { rect in
            NSColor.black.setFill(); NSBezierPath(roundedRect: rect, xRadius: 32, yRadius: 32).fill(); return true
        }
        effect.maskImage = mask
        view.addSubview(effect)
        tint.frame = frame; view.addSubview(tint)
        textView.frame = frame
        textView.content = { [weak self] in (self?.quota.stringValue ?? "", self?.caption.stringValue ?? "") }
        view.addSubview(textView)
        dismissPanel.isReleasedWhenClosed = false; dismissPanel.isOpaque = false
        dismissPanel.backgroundColor = .clear; dismissPanel.hasShadow = false
        dismissPanel.level = .floating; dismissPanel.hidesOnDeactivate = false
        panel.addChildWindow(dismissPanel, ordered: .above)
        let close = NSButton(title: "×", target: self, action: #selector(hide))
        close.isBordered = false; close.frame = NSRect(x: 0, y: 0, width: 20, height: 20)
        close.wantsLayer = true; close.layer?.cornerRadius = 10
        close.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        dismissPanel.contentView = close
        dismissPanel.orderOut(nil)
        view.onRefresh = { [weak self] in self?.refresh() }
        view.onPlacement = { [weak self] point in
            guard let self else { return }
            let left = point.x < self.pet.frame.midX, top = point.y > self.pet.frame.midY
            self.preferences.corner = left ? (top ? .topLeft : .bottomLeft) : (top ? .topRight : .bottomRight)
            self.save(); self.follow()
        }
        pet.isReleasedWhenClosed = false; pet.isOpaque = false; pet.backgroundColor = .clear
        pet.isMovableByWindowBackground = true
        let petView = NSView(frame: NSRect(x: 0, y: 0, width: 80, height: 80))
        petView.wantsLayer = true; petView.layer?.backgroundColor = NSColor.systemOrange.cgColor
        petView.layer?.cornerRadius = 22
        let petText = NSTextField(labelWithString: "🐾")
        petText.font = .systemFont(ofSize: 38); petText.frame = NSRect(x: 15, y: 17, width: 55, height: 48)
        petView.addSubview(petText); pet.contentView = petView
        status.button?.title = "🐾"
        let menu = NSMenu()
        menu.addItem(withTitle: "PetFolio · Mac development", action: nil, keyEquivalent: "")
        for (name, action) in [("Refresh quota", #selector(refresh)), ("Show Quota Bubble", #selector(restore)), ("Hide Quota Bubble", #selector(hide))] {
            let item = menu.addItem(withTitle: name, action: action, keyEquivalent: ""); item.target = self
        }
        let opacity = NSMenuItem(title: "Background opacity", action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        for value in stride(from: 10, through: 100, by: 10) {
            let item = submenu.addItem(withTitle: "\(value)%", action: #selector(setOpacity(_:)), keyEquivalent: "")
            item.tag = value; item.target = self
            opacityItems.append(item)
        }
        opacity.submenu = submenu; menu.addItem(opacity)
        let material = NSMenuItem(title: "Material", action: nil, keyEquivalent: "")
        let materialMenu = NSMenu()
        for (index, value) in SurfaceMaterial.allCases.enumerated() {
            let item = materialMenu.addItem(withTitle: value == .glass ? "Frosted glass" : "Transparent tint (no blur)", action: #selector(setMaterial(_:)), keyEquivalent: "")
            item.tag = index; item.target = self; materialItems.append(item)
        }
        material.submenu = materialMenu; menu.addItem(material)
        let quit = menu.addItem(withTitle: "Exit", action: #selector(exitApp), keyEquivalent: "q"); quit.target = self
        status.menu = menu
        let demoMenuItem = NSMenuItem(title: "Demo pet", action: nil, keyEquivalent: "")
        let demoMenu = NSMenu()
        for (index, name) in ["Orange", "Blue", "Purple", "White"].enumerated() {
            let item = demoMenu.addItem(withTitle: name, action: #selector(selectPet(_:)), keyEquivalent: "")
            item.tag = index; item.target = self
        }
        demoMenuItem.submenu = demoMenu; menu.insertItem(demoMenuItem, at: 1)
        palette.onColor = { [weak self] color in
            guard let self else { return }
            self.petColor = color; self.tint.source = color; self.textView.source = color
            self.pet.contentView?.layer?.backgroundColor = color.color.cgColor
            self.applySurface()
        }
        applySurface()
    }
    func save() { do { try preferences.save(preferencesURL) } catch { caption.stringValue = "Preferences could not be saved" } }
    func timeText(_ date: Date) -> String {
        let formatter = DateFormatter(); formatter.dateFormat = "HH:mm"
        return formatter.string(from: date)
    }
    func follow() {
        textView.needsDisplay = true
        guard !hidden, pet.isVisible else { panel.orderOut(nil); dismissPanel.orderOut(nil); return }
        let screen = NSScreen.screens.first { $0.frame.intersects(pet.frame) } ?? NSScreen.main
        guard let screen else { return }
        panel.setFrame(Placement.resolve(pet: pet.frame, size: panel.frame.size, preferred: preferences.corner, screen: screen.visibleFrame), display: true)
        if !panel.isVisible { panel.orderFrontRegardless() }
        dismissPanel.setFrameOrigin(NSPoint(x: panel.frame.minX - 4, y: panel.frame.maxY - 15))
        let pointer = NSEvent.mouseLocation
        let local = NSPoint(x: pointer.x - panel.frame.minX, y: pointer.y - panel.frame.minY)
        let hover = NSBezierPath(roundedRect: view.bounds, xRadius: 32, yRadius: 32).contains(local)
            || (dismissPanel.isVisible && dismissPanel.frame.contains(pointer))
        if hover { dismissPanel.orderFrontRegardless() } else { dismissPanel.orderOut(nil) }
    }
    func start() {
        do {
            let directory = preferencesURL.deletingLastPathComponent().appendingPathComponent("demo-pets")
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            for (index, color) in demoColors.enumerated() {
                let url = directory.appendingPathComponent("\(index).png")
                try PetPalette.writeDemo(color, to: url); demoImages.append(url)
            }
            selectedImage = ProcessInfo.processInfo.environment["PETFOLIO_PET_SPRITE"].map { URL(fileURLWithPath: $0) } ?? demoImages.first
            updatePalette()
        } catch { fputs("Demo palette setup failed: \(error)\n", stderr) }
        paletteTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.updatePalette() }
        if ci {
            let window = NSWindow(contentRect: NSRect(x: 320, y: 230, width: 430, height: 250), styleMask: [.borderless], backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false; window.contentView = PatternView(frame: NSRect(x: 0, y: 0, width: 430, height: 250))
            window.orderFrontRegardless(); backdrop = window
        }
        pet.orderFrontRegardless(); follow(); refresh()
        followTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.follow() }
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in self?.refresh() }
        if ci { waitForQuota() }
    }
    @objc func hide() { hidden = true; panel.orderOut(nil); dismissPanel.orderOut(nil) }
    @objc func restore() { hidden = false; follow() }
    func applySurface() {
        effect.isHidden = preferences.material != .glass
        // Fade the native material, not the whole window: a full-strength HUD
        // material can look solid even with a nearly transparent extra tint.
        effect.alphaValue = preferences.opacity
        // Keep native blur beneath source-colored tint. Text stays independent.
        tint.opacity = CGFloat(preferences.opacity) * (preferences.material == .glass ? 0.75 : 1)
        for item in opacityItems { item.state = item.tag == Int((preferences.opacity * 100).rounded()) ? .on : .off }
        for item in materialItems { item.state = SurfaceMaterial.allCases[item.tag] == preferences.material ? .on : .off }
        textView.needsDisplay = true
    }
    @objc func setOpacity(_ item: NSMenuItem) { preferences.opacity = Double(item.tag) / 100; applySurface(); save() }
    @objc func setMaterial(_ item: NSMenuItem) { preferences.material = SurfaceMaterial.allCases[item.tag]; applySurface(); save() }
    @objc func selectPet(_ item: NSMenuItem) {
        guard demoImages.indices.contains(item.tag) else { return }
        selectedImage = demoImages[item.tag]; imageStamp = ""; updatePalette()
    }
    func updatePalette() {
        guard let url = selectedImage else { return }
        let attributes = try? FileManager.default.attributesOfItem(atPath: url.path)
        let stamp = "\(url.path)|\(String(describing: attributes?[.modificationDate]))|\(String(describing: attributes?[.size]))"
        guard stamp != imageStamp else { return }; imageStamp = stamp
        palette.select(url)
    }
    @objc func exitApp() { followTimer?.invalidate(); refreshTimer?.invalidate(); paletteTimer?.invalidate(); app.terminate(nil) }
    @objc func refresh() {
        guard !busy else { return }; busy = true; queryCount += 1
        caption.stringValue = "Updating..."
        DispatchQueue.global().async { [weak self] in
            guard let self else { return }
            let result: Result<Quota, Error>
            do {
                result = .success(try self.cli.map { try QuotaClient.read(executable: $0) } ?? Quota(primary: 83, secondary: 61))
            } catch { result = .failure(error) }
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }; self.busy = false
                switch result {
                case .success(let value):
                    self.last = value
                    self.lastChecked = Date()
                    self.quota.stringValue = "5h\t\(Int(value.primary))%\nWeek\t\(Int(value.secondary))%"
                    self.caption.stringValue = "updated " + self.timeText(self.lastChecked!)
                case .failure:
                    self.caption.stringValue = self.last == nil ? "Quota unavailable" : "Failed · last " + self.timeText(self.lastChecked ?? Date())
                    if self.last == nil { self.quota.stringValue = "Unable to read CLI quota\nClick to retry" }
                }
            }
        }
    }
    func snapshot(_ name: String) throws {
        textView.needsDisplay = true
        view.layoutSubtreeIfNeeded()
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw ProbeError.unavailable }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        guard let data = bitmap.representation(using: .png, properties: [:]) else { throw ProbeError.unavailable }
        try data.write(to: output.appendingPathComponent(name))
    }
    func waitForQuota(_ attempt: Int = 0) {
        if busy && attempt < 100 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { self.waitForQuota(attempt + 1) }; return
        }
        do {
            precondition(last?.primary == 83 && last?.secondary == 61)
            caption.stringValue = "updated 17:53"; try snapshot("panel-normal.png")
            caption.stringValue = "Updating..."; try snapshot("panel-updating.png")
            caption.stringValue = "Failed · last 17:53"; try snapshot("panel-failed.png")
            for corner in Corner.allCases {
                preferences.corner = corner; follow(); try snapshot("panel-\(corner.rawValue).png")
            }
            preferences.corner = .topRight; preferences.opacity = 0.6
            applySurface(); save()
            precondition(Preferences.load(preferencesURL).corner == .topRight && Preferences.load(preferencesURL).opacity == 0.6)
            try snapshot("panel-opacity-60.png")
            hide(); precondition(!panel.isVisible); restore(); precondition(panel.isVisible)
            pet.orderOut(nil); follow(); precondition(!panel.isVisible)
            pet.orderFrontRegardless(); follow(); precondition(panel.isVisible)
            caption.stringValue = "updated 17:53"; preferences.opacity = 0.9; applySurface(); save()
            let count = queryCount; refresh(); refresh(); precondition(queryCount == count + 1)
            let result: [String: Any] = ["mode": "native-development", "realCodexPetTested": false,
                "panelVisible": panel.isVisible, "nonactivatingPanel": !panel.canBecomeKey,
                "hideRestorePassed": true, "preferencesPassed": true, "refreshCoalescingPassed": true,
                "reduceTransparency": NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency,
                "syntheticFollowGap": panel.frame.minX - pet.frame.maxX,
                "screens": NSScreen.screens.map { NSStringFromRect($0.frame) }]
            capturePetColors(result: result)
        } catch { fputs("CI verification failed: \(error)\n", stderr); exit(1) }
    }
    func capturePetColors(result: [String: Any], index: Int = 0, attempt: Int = 0) {
        guard index < demoColors.count else { captureSurfaces(result: result); return }
        if attempt == 0 {
            selectedImage = demoImages[index]; imageStamp = ""; updatePalette()
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            let expected = self.demoColors[index]
            guard abs(self.petColor.r - expected.r) + abs(self.petColor.g - expected.g) + abs(self.petColor.b - expected.b) <= 6 else {
                precondition(attempt < 25, "Palette did not settle")
                self.capturePetColors(result: result, index: index, attempt: attempt + 1); return
            }
            precondition(self.textView.source.text.contrast(self.petColor) >= 4.5)
            self.caption.stringValue = "updated 17:53"
            do {
                try self.snapshot("pet-color-\(index).png")
                let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                process.arguments = ["-x", self.output.appendingPathComponent("pet-color-\(index)-desktop.png").path]
                try process.run(); process.waitUntilExit(); precondition(process.terminationStatus == 0)
                self.capturePetColors(result: result, index: index + 1)
            } catch { fputs("Pet color capture failed: \(error)\n", stderr); exit(1) }
        }
    }
    func captureSurfaces(result: [String: Any], index: Int = 0) {
        let cases = [SurfaceMaterial.glass, .tint].flatMap { material in
            [false, true].flatMap { dark in [0.1, 0.6, 1.0].map { (material, dark, $0) } }
        }
        guard index < cases.count else {
            panel.appearance = nil; preferences.material = .glass; preferences.opacity = 0.9; applySurface(); save()
            do {
                try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]).write(to: output.appendingPathComponent("probe.json"))
                print("PROBE_READY"); fflush(stdout)
            } catch { fputs("Surface result failed: \(error)\n", stderr); exit(1) }
            return
        }
        let (material, dark, opacity) = cases[index]
        panel.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        preferences.material = material; preferences.opacity = opacity; applySurface()
        precondition(panel.alphaValue == 1 && textView.alphaValue == 1 && effect.alphaValue == opacity)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
            do {
                let name = "surface-\(material.rawValue)-\(dark ? "dark" : "light")-\(Int(opacity * 100))"
                let process = Process()
                process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                process.arguments = ["-x", self.output.appendingPathComponent(name + ".png").path]
                try process.run(); process.waitUntilExit()
                guard process.terminationStatus == 0 else { throw ProbeError.unavailable }
                self.captureSurfaces(result: result, index: index + 1)
            } catch { fputs("Compositor capture failed: \(error)\n", stderr); exit(1) }
        }
    }
}

let args = Array(CommandLine.arguments.dropFirst())
let ci = args.contains("--ci")
let output = URL(fileURLWithPath: args.first(where: { !$0.hasPrefix("--") }) ?? "/tmp/petfolio-mac-preview")
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let host = Host(output: output, ci: ci, cli: ProcessInfo.processInfo.environment["PETFOLIO_CODEX_EXECUTABLE"])
host.start()
if ci { DispatchQueue.main.asyncAfter(deadline: .now() + 60) { host.exitApp() } }
app.run()
