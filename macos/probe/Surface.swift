import AppKit

// Solid tint fallback. Native glass opacity is controlled on its separate
// visual-effect layer; neither the window nor the text layer is faded.
final class TintView: NSView {
    var opacity: CGFloat = 0.9 { didSet { needsDisplay = true } }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 31.5, yRadius: 31.5)
        NSColor.windowBackgroundColor.withAlphaComponent(opacity).setFill(); path.fill()
        NSColor.labelColor.withAlphaComponent(0.12).setStroke(); path.lineWidth = 1; path.stroke()
    }
}

// CI-only scene behind the real panel. Sharp stripes expose the difference
// between plain transparency and compositor blur in desktop captures.
final class PatternView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedRed: 0.12, green: 0.20, blue: 0.32, alpha: 1).setFill(); bounds.fill()
        let colors = [NSColor.systemPink, .systemTeal, .systemOrange, .systemBlue]
        for column in 0..<Int(bounds.width / 36) + 1 {
            colors[column % colors.count].withAlphaComponent(0.8).setFill()
            NSRect(x: CGFloat(column) * 36, y: 0, width: 18, height: bounds.height).fill()
        }
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 26, weight: .bold), .foregroundColor: NSColor.white]
        for row in 0..<Int(bounds.height / 70) {
            ("BACKGROUND • SHARP STRIPES •" as NSString).draw(at: NSPoint(x: 12, y: CGFloat(row) * 70 + 15), withAttributes: attributes)
        }
    }
}
