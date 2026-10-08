import Foundation
import CoreGraphics

@main struct Tests {
    static func main() throws {
        let root = URL(fileURLWithPath: CommandLine.arguments[1])
        let cli = CommandLine.arguments[2]
        let screen = CGRect(x: 0, y: 0, width: 1024, height: 768), size = CGSize(width: 190, height: 96)
        let pet = CGRect(x: 400, y: 300, width: 80, height: 80)
        for corner in Corner.allCases {
            precondition(Placement.resolve(pet: pet, size: size, preferred: corner, screen: screen) == Placement.frame(pet: pet, size: size, corner: corner))
        }
        let edge = CGRect(x: 930, y: 300, width: 80, height: 80)
        precondition(screen.contains(Placement.resolve(pet: edge, size: size, preferred: .topRight, screen: screen)))
        precondition(Placement.resolve(pet: pet, size: size, preferred: .topRight, screen: screen).minX == pet.maxX + 12)
        let prefs = root.appendingPathComponent("preferences-test.json")
        try Preferences(corner: .bottomLeft, opacity: 0.6).save(prefs)
        precondition(Preferences.load(prefs).corner == .bottomLeft && Preferences.load(prefs).opacity == 0.6)
        try Preferences(corner: .topLeft, opacity: 0.1, material: .tint).save(prefs)
        precondition(Preferences.load(prefs).material == .tint && Preferences.load(prefs).opacity == 0.1)
        try Data("{\"corner\":\"bottomLeft\",\"opacity\":0.6}".utf8).write(to: prefs)
        precondition(Preferences.load(prefs).corner == .bottomLeft && Preferences.load(prefs).material == .glass)
        try Preferences(opacity: 3).save(prefs)
        precondition(Preferences.load(prefs).opacity == 1)
        try Data("broken".utf8).write(to: prefs)
        precondition(Preferences.load(prefs).opacity == 0.9)
        let result = try QuotaClient.read(executable: cli)
        precondition(result.primary == 83 && result.secondary == 61)
        for mode in ["error", "eof", "timeout"] {
            setenv("PETFOLIO_FAKE_MODE", mode, 1)
            var rejected = false
            do { _ = try QuotaClient.read(executable: cli, timeout: 0.3) } catch { rejected = true }
            precondition(rejected)
        }
        unsetenv("PETFOLIO_FAKE_MODE")
        let orange = PetRGB(r: 255, g: 153, b: 0), blue = PetRGB(r: 38, g: 101, b: 190), purple = PetRGB(r: 147, g: 76, b: 182)
        var pixels = [UInt8](repeating: 0, count: 32 * 32 * 4)
        for y in 4..<28 { for x in 4..<28 {
            let i = (y * 32 + x) * 4
            pixels[i] = 255; pixels[i + 1] = 153; pixels[i + 3] = 255
        } }
        precondition(PetPalette.dominant(rgba: pixels, width: 32, height: 32) == orange)
        precondition(PetPalette.dominant(rgba: [UInt8](repeating: 0, count: 4096), width: 32, height: 32) == nil)
        precondition(PetPalette.read(root.appendingPathComponent("missing.png")) == nil)
        for value in [orange, blue, purple, PetRGB(r: 235, g: 235, b: 235), .fallback] {
            precondition(value.contrast(value.text) >= 4.5)
            precondition(value.contrast(value.secondary) >= 4.5)
        }
        var events: [PetRGB] = []
        let source = PetPaletteSource(reader: { url in
            if url.lastPathComponent == "A-old" { Thread.sleep(forTimeInterval: 0.25); return orange }
            if url.lastPathComponent == "B" { Thread.sleep(forTimeInterval: 0.1); return blue }
            return purple
        })
        source.onColor = { events.append($0) }
        for name in ["A-old", "B", "A-new"] { source.select(root.appendingPathComponent(name)) }
        let deadline = Date().addingTimeInterval(0.5)
        while Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.01)) }
        precondition(events.last == purple && !events.contains(orange) && !events.contains(blue))
        print("PASS: placement, edge fallback, preferred restoration, preferences, stdio success/error/EOF/timeout")
        print("PASS: transparent-image sampling, missing-image fallback, text contrast, asynchronous stale A/B/A rejection")
    }
}
