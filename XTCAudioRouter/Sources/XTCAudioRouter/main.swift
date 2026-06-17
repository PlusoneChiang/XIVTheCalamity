//
//  main.swift
//  XTCAudioRouter
//
//  macOS Audio Routing Helper for XIV The Calamity
//  Monitors system audio device changes and updates Wine audio settings
//

import Foundation

// Global references for signal handler access
private var globalAudioRouter: AudioRouter?
private var globalProcessMonitor: ProcessMonitor?

private func logMessage(_ message: String) {
    xtcLog(label: "XTCAudioRouter", message)
}

private func parseArgs() -> (pid: Int32, wineprefix: String, wine: String, msync: Bool)? {
    var pid: Int32?
    var wineprefix: String?
    var wine: String?
    var msync = false
    let args = CommandLine.arguments.dropFirst()
    var i = args.startIndex
    while i < args.endIndex {
        switch args[i] {
        case "--pid":
            args.formIndex(after: &i)
            guard i < args.endIndex, let v = Int32(args[i]) else {
                fputs("Error: --pid requires an integer\n", stderr); return nil
            }
            pid = v
        case "--wineprefix":
            args.formIndex(after: &i)
            guard i < args.endIndex else { fputs("Error: --wineprefix requires a value\n", stderr); return nil }
            wineprefix = args[i]
        case "--wine":
            args.formIndex(after: &i)
            guard i < args.endIndex else { fputs("Error: --wine requires a value\n", stderr); return nil }
            wine = args[i]
        case "--msync":
            msync = true
        default:
            fputs("Error: unknown argument \(args[i])\n", stderr); return nil
        }
        args.formIndex(after: &i)
    }
    guard let p = pid, let wp = wineprefix, let w = wine else {
        fputs("Usage: XTCAudioRouter --pid <pid> --wineprefix <path> --wine <path> [--msync]\n", stderr)
        return nil
    }
    return (p, wp, w, msync)
}

guard let opts = parseArgs() else { exit(1) }

logMessage("XTCAudioRouter starting...")
logMessage("PID: \(opts.pid)")
logMessage("Wine Prefix: \(opts.wineprefix)")
logMessage("Wine: \(opts.wine)")
logMessage("Msync: \(opts.msync)")

guard FileManager.default.fileExists(atPath: opts.wineprefix) else {
    logMessage("Error: Wine prefix path does not exist: \(opts.wineprefix)")
    exit(1)
}
guard FileManager.default.fileExists(atPath: opts.wine) else {
    logMessage("Error: Wine executable does not exist: \(opts.wine)")
    exit(1)
}

let wineRegistry = WineRegistry(winePath: opts.wine, winePrefix: opts.wineprefix, msync: opts.msync)
let audioRouter = AudioRouter(wineRegistry: wineRegistry)
let processMonitor = ProcessMonitor(pid: opts.pid)

globalAudioRouter = audioRouter
globalProcessMonitor = processMonitor

guard audioRouter.start() else {
    logMessage("Error: Failed to start audio routing")
    exit(1)
}

func setupSignalHandlers() {
    signal(SIGTERM, signalHandler)
    signal(SIGINT, signalHandler)
}

func signalHandler(_ signal: Int32) {
    logMessage("Received signal \(signal), shutting down...")
    shutdown()
}

func shutdown() {
    globalAudioRouter?.stop()
    globalProcessMonitor?.stop()
    Darwin.exit(0)
}

setupSignalHandlers()

processMonitor.start(
    onDetected: {
        logMessage("Game process detected, triggering initial audio device rescan...")
        audioRouter.forceRescan()
    },
    onExit: {
        logMessage("Game process exited, shutting down...")
        shutdown()
    }
)

logMessage("Audio routing active. Monitoring PID \(opts.pid)...")
RunLoop.main.run()
