import AppKit

final class DisplayLabel: NSTextField {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
final class DisplayEffect: NSVisualEffectView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
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
        guard NSBezierPath(roundedRect: bounds, xRadius: 24, yRadius: 24).contains(local) else { return nil }
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
        else if NSBezierPath(roundedRect: bounds, xRadius: 24, yRadius: 24).contains(convert(event.locationInWindow, from: nil)) { onRefresh?() }
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
    let title = DisplayLabel(labelWithString: "Quota Bubble")
    let quota = DisplayLabel(labelWithString: "No quota yet")
    let caption = DisplayLabel(labelWithString: "Demo · simulated pet")
    let pet: NSWindow
    let status: NSStatusItem
    var hidden = false, busy = false
    var last: Quota?
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
        let frame = NSRect(x: 0, y: 0, width: 260, height: 144)
        panel = BubblePanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        view = BubbleView(frame: frame)
        effect = DisplayEffect(frame: frame)
        pet = NSWindow(contentRect: NSRect(x: 400, y: 300, width: 80, height: 80), styleMask: [.borderless], backing: .buffered, defer: false)
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        panel.isReleasedWhenClosed = false; panel.isOpaque = false; panel.backgroundColor = .clear
        panel.hasShadow = true; panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false; panel.contentView = view
        effect.material = .hudWindow; effect.blendingMode = .behindWindow; effect.state = .active
        effect.wantsLayer = true; effect.layer?.cornerRadius = 24; effect.layer?.masksToBounds = true
        effect.alphaValue = preferences.opacity
        view.addSubview(effect)
        title.frame = NSRect(x: 20, y: 108, width: 210, height: 22)
        title.font = .systemFont(ofSize: 15, weight: .semibold)
        quota.frame = NSRect(x: 20, y: 43, width: 230, height: 54)
        quota.font = .monospacedDigitSystemFont(ofSize: 20, weight: .medium)
        caption.frame = NSRect(x: 20, y: 15, width: 225, height: 18)
        caption.font = .systemFont(ofSize: 11); caption.textColor = .secondaryLabelColor
        // Text is drawn directly so it does not consume mouse clicks.
        for label in [title, quota, caption] { view.addSubview(label) }
        let close = NSButton(title: "×", target: self, action: #selector(hide))
        close.isBordered = false; close.frame = NSRect(x: 229, y: 105, width: 26, height: 26)
        view.addSubview(close)
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
        }
        opacity.submenu = submenu; menu.addItem(opacity)
        let quit = menu.addItem(withTitle: "Exit", action: #selector(exitApp), keyEquivalent: "q"); quit.target = self
        status.menu = menu
    }
    func save() { do { try preferences.save(preferencesURL) } catch { caption.stringValue = "Preferences could not be saved" } }
    func follow() {
        guard !hidden, pet.isVisible else { panel.orderOut(nil); return }
        let screen = NSScreen.screens.first { $0.frame.intersects(pet.frame) } ?? NSScreen.main
        guard let screen else { return }
        panel.setFrame(Placement.resolve(pet: pet.frame, size: panel.frame.size, preferred: preferences.corner, screen: screen.visibleFrame), display: true)
        if !panel.isVisible { panel.orderFrontRegardless() }
    }
    func start() {
        pet.orderFrontRegardless(); follow(); refresh()
        followTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.follow() }
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in self?.refresh() }
        if ci { waitForQuota() }
    }
    @objc func hide() { hidden = true; panel.orderOut(nil) }
    @objc func restore() { hidden = false; follow() }
    @objc func setOpacity(_ item: NSMenuItem) { preferences.opacity = Double(item.tag) / 100; effect.alphaValue = preferences.opacity; save() }
    @objc func exitApp() { followTimer?.invalidate(); refreshTimer?.invalidate(); app.terminate(nil) }
    @objc func refresh() {
        guard !busy else { return }; busy = true; queryCount += 1
        caption.stringValue = "Updating…"
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
                    self.quota.stringValue = "5h remaining \(Int(value.primary))%\nWeekly remaining \(Int(value.secondary))%"
                    self.caption.stringValue = "Demo · simulated pet"
                case .failure:
                    self.caption.stringValue = self.last == nil ? "Quota unavailable · click to retry" : "Failed · showing previous quota"
                }
            }
        }
    }
    func snapshot(_ name: String) throws {
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
            try snapshot("panel-normal.png")
            caption.stringValue = "Updating…"; try snapshot("panel-updating.png")
            caption.stringValue = "Failed · showing previous quota"; try snapshot("panel-failed.png")
            for corner in Corner.allCases {
                preferences.corner = corner; follow(); try snapshot("panel-\(corner.rawValue).png")
            }
            preferences.corner = .topRight; preferences.opacity = 0.6
            effect.alphaValue = preferences.opacity; save()
            precondition(Preferences.load(preferencesURL).corner == .topRight && Preferences.load(preferencesURL).opacity == 0.6)
            try snapshot("panel-opacity-60.png")
            hide(); precondition(!panel.isVisible); restore(); precondition(panel.isVisible)
            pet.orderOut(nil); follow(); precondition(!panel.isVisible)
            pet.orderFrontRegardless(); follow(); precondition(panel.isVisible)
            caption.stringValue = "Demo · simulated pet"; preferences.opacity = 0.9; effect.alphaValue = 0.9; save()
            let count = queryCount; refresh(); refresh(); precondition(queryCount == count + 1)
            let result: [String: Any] = ["mode": "native-development", "realCodexPetTested": false,
                "panelVisible": panel.isVisible, "nonactivatingPanel": !panel.canBecomeKey,
                "hideRestorePassed": true, "preferencesPassed": true, "refreshCoalescingPassed": true,
                "syntheticFollowGap": panel.frame.minX - pet.frame.maxX,
                "screens": NSScreen.screens.map { NSStringFromRect($0.frame) }]
            try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]).write(to: output.appendingPathComponent("probe.json"))
            print("PROBE_READY"); fflush(stdout)
        } catch { fputs("CI verification failed: \(error)\n", stderr); exit(1) }
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
