import AppKit

struct PetRGB: Equatable {
    let r: Double, g: Double, b: Double
    var color: NSColor { NSColor(srgbRed: r / 255, green: g / 255, blue: b / 255, alpha: 1) }
    static let fallback = PetRGB(r: 27, g: 31, b: 38)
    var luminance: Double {
        func channel(_ n: Double) -> Double { let s = n / 255; return s <= 0.04045 ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4) }
        return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
    }
    func contrast(_ other: PetRGB) -> Double { (max(luminance, other.luminance) + 0.05) / (min(luminance, other.luminance) + 0.05) }
    var text: PetRGB {
        let black = PetRGB(r: 0, g: 0, b: 0), white = PetRGB(r: 255, g: 255, b: 255)
        return contrast(black) >= contrast(white) ? black : white
    }
    var secondary: PetRGB {
        let main = text
        let soft = PetRGB(r: (r + (main.r - r) * 0.75).rounded(), g: (g + (main.g - g) * 0.75).rounded(), b: (b + (main.b - b) * 0.75).rounded())
        return contrast(soft) >= 4.5 ? soft : main
    }
}

enum PetPalette {
    static func dominant(rgba: [UInt8], width: Int, height: Int) -> PetRGB? {
        guard width >= 8, height >= 8, rgba.count == width * height * 4 else { return nil }
        var weights = [Double](repeating: 0, count: 24), red = weights, green = weights, blue = weights
        var neutral = [Double](repeating: 0, count: 3), neutralCount = 0, visible = 0
        for y in stride(from: 0, to: height, by: 2) { for x in stride(from: 0, to: width, by: 2) {
            let i = (y * width + x) * 4, alpha = Double(rgba[i + 3]) / 255
            guard alpha >= 32.0 / 255 else { continue }
            // CGContext stores premultiplied components; recover source RGB.
            let r = min(255, Double(rgba[i]) / alpha), g = min(255, Double(rgba[i + 1]) / alpha), b = min(255, Double(rgba[i + 2]) / alpha)
            let hi = max(r, g, b), lo = min(r, g, b)
            guard hi >= 35 else { continue }; visible += 1
            let saturation = (hi - lo) / hi
            if saturation < 0.18 { neutral[0] += r; neutral[1] += g; neutral[2] += b; neutralCount += 1; continue }
            let delta = hi - lo
            var hue = hi == r ? (g - b) / delta : hi == g ? 2 + (b - r) / delta : 4 + (r - g) / delta
            hue *= 60; if hue < 0 { hue += 360 }
            let bin = Int((hue + 7.5) / 15) % 24, weight = 0.35 + 0.65 * saturation
            weights[bin] += weight; red[bin] += weight * r; green[bin] += weight * g; blue[bin] += weight * b
        } }
        guard visible >= 20 else { return nil }
        let best = weights.indices.max { weights[$0] < weights[$1] }!
        if weights[best] >= max(12, Double(visible) * 0.06) {
            return PetRGB(r: floor(red[best] / weights[best]), g: floor(green[best] / weights[best]), b: floor(blue[best] / weights[best]))
        }
        guard neutralCount >= 20 else { return nil }
        return PetRGB(r: floor(neutral[0] / Double(neutralCount)), g: floor(neutral[1] / Double(neutralCount)), b: floor(neutral[2] / Double(neutralCount)))
    }
    static func read(_ url: URL) -> PetRGB? {
        guard let image = NSImage(contentsOf: url), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        // Bound sampling cost independently of sprite-sheet dimensions.
        let width = min(256, cg.width), height = min(256, cg.height)
        var bytes = [UInt8](repeating: 0, count: width * height * 4)
        let rendered = bytes.withUnsafeMutableBytes { data -> Bool in
            guard let context = CGContext(data: data.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            context.draw(cg, in: CGRect(x: 0, y: 0, width: width, height: height)); return true
        }
        return rendered ? dominant(rgba: bytes, width: width, height: height) : nil
    }
}

struct PaletteGeneration {
    private(set) var current: UInt64 = 0
    mutating func begin() -> UInt64 { current += 1; return current }
    func accepts(_ generation: UInt64) -> Bool { generation == current }
}
final class PetPaletteSource {
    private var generation = PaletteGeneration()
    private let reader: (URL) -> PetRGB?
    init(reader: @escaping (URL) -> PetRGB? = PetPalette.read) { self.reader = reader }
    var onColor: ((PetRGB) -> Void)?
    func select(_ url: URL) {
        let token = generation.begin()
        onColor?(.fallback)
        let read = reader
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let color = read(url) ?? .fallback
            DispatchQueue.main.async { [weak self] in
                guard let self, self.generation.accepts(token) else { return }
                self.onColor?(color)
            }
        }
    }
}
