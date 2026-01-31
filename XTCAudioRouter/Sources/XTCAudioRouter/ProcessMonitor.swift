//
//  ProcessMonitor.swift
//  XTCAudioRouter
//
//  Monitor game process and exit when game exits
//

import Foundation

/// Process monitor for tracking game process
class ProcessMonitor {
    
    private let targetPID: Int32
    private var timer: DispatchSourceTimer?
    private var onProcessExit: (() -> Void)?
    
    /// Check interval in seconds
    private let checkInterval: TimeInterval = 2.0
    
    init(pid: Int32) {
        self.targetPID = pid
    }
    
    // MARK: - Public Methods
    
    /// Start monitoring the process
    /// - Parameter onExit: Callback when process exits
    func start(onExit: @escaping () -> Void) {
        self.onProcessExit = onExit
        
        // Check if process exists initially
        guard isProcessRunning(pid: targetPID) else {
            log("Process \(targetPID) not found, exiting immediately")
            onExit()
            return
        }
        
        log("Started monitoring PID \(targetPID)")
        
        // Create timer for periodic checks
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
        timer.schedule(deadline: .now() + checkInterval, repeating: checkInterval)
        timer.setEventHandler { [weak self] in
            self?.checkProcess()
        }
        timer.resume()
        self.timer = timer
    }
    
    /// Stop monitoring
    func stop() {
        timer?.cancel()
        timer = nil
        log("Stopped monitoring")
    }
    
    // MARK: - Private Methods
    
    private func checkProcess() {
        if !isProcessRunning(pid: targetPID) {
            log("Process \(targetPID) has exited")
            timer?.cancel()
            timer = nil
            
            DispatchQueue.main.async { [weak self] in
                self?.onProcessExit?()
            }
        }
    }
    
    /// Check if a process with given PID is running
    private func isProcessRunning(pid: Int32) -> Bool {
        // Use kill with signal 0 to check if process exists
        // Returns 0 if process exists, -1 if not
        return kill(pid, 0) == 0
    }
    
    private func log(_ message: String) {
        let timestamp = ISO8601DateFormatter().string(from: Date())
        print("[\(timestamp)] [ProcessMonitor] \(message)")
        fflush(stdout)
    }
}
