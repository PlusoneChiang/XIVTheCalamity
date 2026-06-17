// Shared logging utility for XTCAudioRouter
import Foundation

private let _isoFormatter = ISO8601DateFormatter()

func xtcLog(label: String, _ message: String) {
    let timestamp = _isoFormatter.string(from: Date())
    print("[\(timestamp)] [\(label)] \(message)")
    fflush(stdout)
}
