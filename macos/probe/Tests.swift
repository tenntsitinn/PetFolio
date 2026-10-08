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
        print("PASS: placement, edge fallback, preferred restoration, preferences, stdio success/error/EOF/timeout")
    }
}
