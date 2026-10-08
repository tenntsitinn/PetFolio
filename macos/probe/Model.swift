import Foundation
import CoreGraphics

enum Corner: String, Codable, CaseIterable {
    case topLeft, topRight, bottomLeft, bottomRight
}
enum SurfaceMaterial: String, Codable, CaseIterable { case glass, tint }
struct Preferences: Codable {
    var corner: Corner = .topRight
    var opacity: Double = 0.9
    var material: SurfaceMaterial = .glass
    enum CodingKeys: String, CodingKey { case corner, opacity, material }
    init(corner: Corner = .topRight, opacity: Double = 0.9, material: SurfaceMaterial = .glass) {
        self.corner = corner; self.opacity = opacity; self.material = material
    }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        corner = try values.decodeIfPresent(Corner.self, forKey: .corner) ?? .topRight
        opacity = try values.decodeIfPresent(Double.self, forKey: .opacity) ?? 0.9
        material = try values.decodeIfPresent(SurfaceMaterial.self, forKey: .material) ?? .glass
    }
    static func load(_ url: URL) -> Preferences {
        guard let data = try? Data(contentsOf: url), var value = try? JSONDecoder().decode(Self.self, from: data) else { return Self() }
        value.opacity = min(1, max(0.1, value.opacity.isFinite ? value.opacity : 0.9))
        return value
    }
    func save(_ url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(self).write(to: url, options: .atomic)
    }
}
struct Placement {
    static func frame(pet: CGRect, size: CGSize, corner: Corner) -> CGRect {
        let left = corner == .topLeft || corner == .bottomLeft
        let top = corner == .topLeft || corner == .topRight
        return CGRect(x: left ? pet.minX - size.width - 12 : pet.maxX + 12,
                      y: top ? pet.maxY - size.height : pet.minY, width: size.width, height: size.height)
    }
    static func resolve(pet: CGRect, size: CGSize, preferred: Corner, screen: CGRect) -> CGRect {
        let candidates = [preferred] + Corner.allCases.filter { $0 != preferred }
        for corner in candidates {
            let frame = frame(pet: pet, size: size, corner: corner)
            if screen.contains(frame) { return frame }
        }
        var value = frame(pet: pet, size: size, corner: preferred)
        value.origin.x = max(screen.minX, min(value.minX, screen.maxX - size.width))
        value.origin.y = max(screen.minY, min(value.minY, screen.maxY - size.height))
        return value
    }
}
struct Quota {
    let primary: Double
    let secondary: Double
    static func parse(_ value: [String: Any]) throws -> Quota {
        let indexed = value["rateLimitsByLimitId"] as? [String: Any]
        guard let bucket = (indexed?["codex"] ?? value["rateLimits"]) as? [String: Any],
              let p = bucket["primary"] as? [String: Any], let s = bucket["secondary"] as? [String: Any],
              let primary = p["usedPercent"] as? Double, let secondary = s["usedPercent"] as? Double,
              primary.isFinite, secondary.isFinite else { throw ProbeError.unavailable }
        return Quota(primary: min(100, max(0, 100 - primary)), secondary: min(100, max(0, 100 - secondary)))
    }
}
enum ProbeError: Error { case unavailable, rejected, eof, timeout }

// Separate protocol/process ownership from views. Development uses a fake CLI.
enum QuotaClient {
    static func read(executable: String, timeout: TimeInterval = 5) throws -> Quota {
        let process = Process(), input = Pipe(), output = Pipe(), errors = Pipe()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = ["app-server", "--stdio"]
        process.standardInput = input; process.standardOutput = output; process.standardError = errors
        errors.fileHandleForReading.readabilityHandler = { handle in _ = handle.availableData }
        try process.run()
        let timeoutSignal = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            if timeoutSignal.wait(timeout: .now() + timeout) == .timedOut, process.isRunning { process.terminate() }
        }
        defer {
            timeoutSignal.signal()
            try? input.fileHandleForWriting.close()
            if process.isRunning { process.terminate() }
            process.waitUntilExit()
            errors.fileHandleForReading.readabilityHandler = nil
        }
        func send(_ value: [String: Any]) throws {
            var data = try JSONSerialization.data(withJSONObject: value); data.append(10)
            try input.fileHandleForWriting.write(contentsOf: data)
        }
        let started = Date()
        try send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "PetFolio", "version": "mac-dev"]]])
        var initialized = false, buffer = Data()
        while true {
            let byte = output.fileHandleForReading.readData(ofLength: 1)
            if byte.isEmpty { throw Date().timeIntervalSince(started) >= timeout ? ProbeError.timeout : ProbeError.eof }
            buffer.append(byte)
            if byte[0] != 10 { continue }
            defer { buffer.removeAll(keepingCapacity: true) }
            guard let message = (try? JSONSerialization.jsonObject(with: buffer)) as? [String: Any],
                  let id = message["id"] as? Int, id == (initialized ? 2 : 1) else { continue }
            if message["error"] != nil { throw ProbeError.rejected }
            if !initialized {
                initialized = true
                try send(["method": "initialized"])
                try send(["id": 2, "method": "account/rateLimits/read"])
            } else {
                guard let result = message["result"] as? [String: Any] else { throw ProbeError.unavailable }
                return try Quota.parse(result)
            }
        }
    }
}
