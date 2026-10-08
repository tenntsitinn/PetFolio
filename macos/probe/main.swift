import AppKit

// CI-only feasibility probe: synthetic quota and pet, no Codex account access.
let output = CommandLine.arguments.dropFirst().first ?? "artifacts"
try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
let app = NSApplication.shared
app.setActivationPolicy(.regular)
let panel = NSWindow(contentRect: NSRect(x: 240, y: 240, width: 300, height: 150),
                     styleMask: [.titled, .closable], backing: .buffered, defer: false)
panel.title = "PetFolio macOS CI Probe"
panel.isReleasedWhenClosed = false
let effect = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: 300, height: 150))
effect.material = .hudWindow
effect.blendingMode = .behindWindow
effect.state = .active
effect.wantsLayer = true
effect.layer?.cornerRadius = 20
panel.contentView = effect
let title = NSTextField(labelWithString: "PetFolio · Synthetic quota")
title.frame = NSRect(x: 20, y: 100, width: 270, height: 24)
title.font = .systemFont(ofSize: 16, weight: .semibold)
effect.addSubview(title)
let quota = NSTextField(labelWithString: "5h remaining 83%\nWeekly remaining 61%")
quota.frame = NSRect(x: 20, y: 35, width: 270, height: 54)
quota.font = .monospacedDigitSystemFont(ofSize: 18, weight: .regular)
effect.addSubview(quota)
let pet = NSWindow(contentRect: NSRect(x: 160, y: 240, width: 64, height: 64),
                   styleMask: [.borderless], backing: .buffered, defer: false)
pet.isReleasedWhenClosed = false
pet.backgroundColor = .systemOrange
let petLabel = NSTextField(labelWithString: "PET")
petLabel.frame = NSRect(x: 10, y: 20, width: 50, height: 24)
pet.contentView?.addSubview(petLabel)
panel.makeKeyAndOrderFront(nil)
pet.orderFrontRegardless()
app.activate(ignoringOtherApps: true)

func snapshot(_ name: String) throws {
    effect.layoutSubtreeIfNeeded()
    guard let bitmap = effect.bitmapImageRepForCachingDisplay(in: effect.bounds) else {
        throw NSError(domain: "probe", code: 1)
    }
    effect.cacheDisplay(in: effect.bounds, to: bitmap)
    guard let data = bitmap.representation(using: .png, properties: [:]) else {
        throw NSError(domain: "probe", code: 2)
    }
    try data.write(to: URL(fileURLWithPath: output).appendingPathComponent(name))
}
DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
    do {
        try snapshot("panel-normal.png")
        quota.stringValue = "Updating…\nLast known: 83% / 61%"
        try snapshot("panel-updating.png")
        quota.stringValue = "Failed · last 12:34\nLast known: 83% / 61%"
        try snapshot("panel-failed.png")
        quota.stringValue = "5h remaining 83%\nWeekly remaining 61%"
        pet.setFrameOrigin(NSPoint(x: 400, y: 300))
        panel.setFrameOrigin(NSPoint(x: pet.frame.maxX + 16, y: pet.frame.minY))
        let record: [String: Any] = [
            "mode": "synthetic-probe", "realCodexPetTested": false,
            "windowNumber": panel.windowNumber,
            "panelVisible": panel.isVisible,
            "syntheticFollowGap": panel.frame.minX - pet.frame.maxX,
            "screens": NSScreen.screens.map { NSStringFromRect($0.frame) }
        ]
        let data = try JSONSerialization.data(withJSONObject: record, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: URL(fileURLWithPath: output).appendingPathComponent("probe.json"))
        print("PROBE_READY")
        fflush(stdout)
    } catch { fputs("Probe failed: \(error)\n", stderr); exit(1) }
}
DispatchQueue.main.asyncAfter(deadline: .now() + 40) { app.terminate(nil) }
app.run()
