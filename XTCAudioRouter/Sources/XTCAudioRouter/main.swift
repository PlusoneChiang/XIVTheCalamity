//
//  main.swift
//  XTCAudioRouter
//
//  macOS Audio Routing Helper for XIV The Calamity
//  Monitors system audio device changes and updates Wine audio settings
//

import ArgumentParser
import Foundation

// Global references for signal handler access
private var globalAudioRouter: AudioRouter?
private var globalProcessMonitor: ProcessMonitor?

struct XTCAudioRouter: ParsableCommand {
    
    static var configuration = CommandConfiguration(
        commandName: "XTCAudioRouter",
        abstract: "Audio routing helper for XIV The Calamity",
        discussion: """
            Monitors system audio device changes and automatically updates 
            Wine audio output settings. Exits when the monitored game process terminates.
            """
    )
    
    @Option(name: .long, help: "Game process ID to monitor")
    var pid: Int32
    
    @Option(name: .long, help: "Wine prefix path")
    var wineprefix: String
    
    @Option(name: .long, help: "Wine executable path")
    var wine: String
    
    @Flag(name: .long, help: "Enable Wine esync")
    var esync: Bool = false
    
    @Flag(name: .long, help: "Enable Wine msync")
    var msync: Bool = false
    
    func run() throws {
        logMessage("XTCAudioRouter starting...")
        logMessage("PID: \(pid)")
        logMessage("Wine Prefix: \(wineprefix)")
        logMessage("Wine: \(wine)")
        logMessage("Esync: \(esync), Msync: \(msync)")
        
        // Validate paths
        guard FileManager.default.fileExists(atPath: wineprefix) else {
            logMessage("Error: Wine prefix path does not exist: \(wineprefix)")
            throw ExitCode.failure
        }
        
        guard FileManager.default.fileExists(atPath: wine) else {
            logMessage("Error: Wine executable does not exist: \(wine)")
            throw ExitCode.failure
        }
        
        // Initialize components
        let wineRegistry = WineRegistry(winePath: wine, winePrefix: wineprefix, esync: esync, msync: msync)
        let audioRouter = AudioRouter(wineRegistry: wineRegistry)
        let processMonitor = ProcessMonitor(pid: pid)
        
        // Store global references for signal handlers
        globalAudioRouter = audioRouter
        globalProcessMonitor = processMonitor
        
        // Start audio routing
        guard audioRouter.start() else {
            logMessage("Error: Failed to start audio routing")
            throw ExitCode.failure
        }
        
        // Setup signal handlers for graceful shutdown
        setupSignalHandlers()
        
        // Start process monitoring
        processMonitor.start {
            logMessage("Game process exited, shutting down...")
            shutdown()
        }
        
        logMessage("Audio routing active. Monitoring PID \(pid)...")
        
        // Keep running until process exits or signal received
        RunLoop.main.run()
    }
    
    private func setupSignalHandlers() {
        // Handle SIGTERM
        signal(SIGTERM, signalHandler)
        
        // Handle SIGINT (Ctrl+C)
        signal(SIGINT, signalHandler)
    }
}

private func signalHandler(_ signal: Int32) {
    logMessage("Received signal \(signal), shutting down...")
    shutdown()
}

private func shutdown() {
    globalAudioRouter?.stop()
    globalProcessMonitor?.stop()
    Darwin.exit(0)
}

private func logMessage(_ message: String) {
    let timestamp = ISO8601DateFormatter().string(from: Date())
    print("[\(timestamp)] [XTCAudioRouter] \(message)")
    fflush(stdout)  // Ensure immediate output
}

// Entry point
XTCAudioRouter.main()
