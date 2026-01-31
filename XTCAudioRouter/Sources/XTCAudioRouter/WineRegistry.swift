//
//  WineRegistry.swift
//  XTCAudioRouter
//
//  Wine Registry operations:
//  - Write: Uses `wine reg` command (proper wineserver interaction)
//  - Read: Direct file parsing of user.reg (fast GUID lookup)
//

import Foundation

/// Wine Registry operations
/// Uses `wine reg` command for writes (reliable), direct file read for queries (fast)
class WineRegistry {
    
    private let winePath: String
    private let winePrefix: String
    private let userRegPath: String
    private let esync: Bool
    private let msync: Bool
    
    /// Base registry key for Wine CoreAudio driver
    private let wineDriverKey = #"HKEY_CURRENT_USER\Software\Wine\Drivers\winecoreaudio.drv"#
    
    /// Counter for RescanDevices toggle
    private var rescanCounter: Int = 0
    
    init(winePath: String, winePrefix: String, esync: Bool = true, msync: Bool = true) {
        self.winePath = winePath
        self.winePrefix = winePrefix
        self.userRegPath = (winePrefix as NSString).appendingPathComponent("user.reg")
        self.esync = esync
        self.msync = msync
    }
    
    // MARK: - Public Methods
    
    /// Set Wine default output device
    /// - Parameter guid: Wine GUID for the device
    func setDefaultOutput(guid: String) {
        let deviceID = "{0.0.0.00000000}.{\(guid)}"
        runWineReg(key: wineDriverKey, value: "DefaultOutput", data: deviceID)
        log("Set DefaultOutput to \(deviceID)")
    }
    
    /// Trigger Wine to rescan audio devices
    func rescanDevices() {
        rescanCounter = (rescanCounter == 0) ? 1 : 0
        runWineRegDword(key: wineDriverKey, value: "RescanDevices", data: rescanCounter)
        log("Triggered RescanDevices (\(rescanCounter))")
    }
    
    /// Create device GUID mapping in Wine Registry
    /// - Parameters:
    ///   - coreAudioUID: CoreAudio device UID
    ///   - guid: Wine GUID
    func createDeviceMapping(coreAudioUID: String, guid: String) {
        let deviceKey = "\(wineDriverKey)\\devices\\0,\(coreAudioUID)"
        let hexData = guidToHexString(guid)
        runWineRegBinary(key: deviceKey, value: "guid", hexData: hexData)
        log("Created device mapping: \(coreAudioUID) -> \(guid)")
    }
    
    /// Read existing GUID from Wine Registry (user.reg file)
    /// Direct file read is faster than wine reg query
    /// - Parameter coreAudioUID: CoreAudio device UID
    /// - Returns: Existing GUID if found
    func readExistingGUID(for coreAudioUID: String) -> String? {
        guard let content = try? String(contentsOfFile: userRegPath, encoding: .utf8) else {
            return nil
        }
        
        // Wine registry path format: [Software\\Wine\\Drivers\\winecoreaudio.drv\\devices\\0,<UID>]
        let escapedUID = coreAudioUID
            .replacingOccurrences(of: "\\", with: "\\\\\\\\")
            .replacingOccurrences(of: ".", with: "\\.")
            .replacingOccurrences(of: "-", with: "\\-")
        let sectionPattern = "\\[Software\\\\\\\\Wine\\\\\\\\Drivers\\\\\\\\winecoreaudio\\.drv\\\\\\\\devices\\\\\\\\0,\(escapedUID)\\]"
        
        guard let sectionRange = content.range(of: sectionPattern, options: .regularExpression) else {
            return nil
        }
        
        let sectionStart = sectionRange.upperBound
        let remainingContent = String(content[sectionStart...])
        
        // Find next section
        let nextSectionRange = remainingContent.range(of: "\n[", options: [])
        let sectionContent: String
        if let nextRange = nextSectionRange {
            sectionContent = String(remainingContent[..<nextRange.lowerBound])
        } else {
            sectionContent = remainingContent
        }
        
        // Search for "guid"=hex:XX,XX,XX,...
        let guidPattern = #""guid"=hex:([0-9a-fA-F,]+)"#
        guard let guidMatch = sectionContent.range(of: guidPattern, options: .regularExpression) else {
            return nil
        }
        
        let matchedString = String(sectionContent[guidMatch])
        guard let hexStart = matchedString.range(of: "hex:")?.upperBound else {
            return nil
        }
        let hexData = String(matchedString[hexStart...]).replacingOccurrences(of: ",", with: "")
        
        return hexStringToGUID(hexData)
    }
    
    // MARK: - Wine Reg Commands (Write Operations)
    
    /// Run wine reg add command for string value
    private func runWineReg(key: String, value: String, data: String) {
        // wine reg add "KEY" /v "VALUE" /d "DATA" /f
        let args = ["reg", "add", key, "/v", value, "/d", data, "/f"]
        runWineCommand(args: args)
    }
    
    /// Run wine reg add command for DWORD value
    private func runWineRegDword(key: String, value: String, data: Int) {
        // wine reg add "KEY" /v "VALUE" /t REG_DWORD /d DATA /f
        let args = ["reg", "add", key, "/v", value, "/t", "REG_DWORD", "/d", String(data), "/f"]
        runWineCommand(args: args)
    }
    
    /// Run wine reg add command for binary value
    private func runWineRegBinary(key: String, value: String, hexData: String) {
        // wine reg add "KEY" /v "VALUE" /t REG_BINARY /d HEXDATA /f
        let args = ["reg", "add", key, "/v", value, "/t", "REG_BINARY", "/d", hexData, "/f"]
        runWineCommand(args: args)
    }
    
    /// Execute wine command using shell (more compatible)
    /// Now runs asynchronously to avoid blocking when wineserver is busy
    private func runWineCommand(args: [String]) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        
        // Build the command string with proper escaping
        let wineArgs = args.map { arg in
            // Escape special characters for shell
            if arg.contains(" ") || arg.contains("\\") || arg.contains("{") || arg.contains("}") {
                return "'\(arg)'"
            }
            return arg
        }.joined(separator: " ")
        
        // Build environment variables based on settings
        let esyncValue = esync ? "1" : "0"
        let msyncValue = msync ? "1" : "0"
        
        // Include WINEESYNC and WINEMSYNC to match game environment
        let command = "WINEPREFIX='\(winePrefix)' WINEDEBUG=-all WINEESYNC=\(esyncValue) WINEMSYNC=\(msyncValue) '\(winePath)' \(wineArgs)"
        process.arguments = ["-c", command]
        
        // Inherit current environment
        process.environment = ProcessInfo.processInfo.environment
        
        // Capture output for debugging
        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()
        process.standardOutput = stdoutPipe
        process.standardError = stderrPipe
        
        do {
            log("Running (async): \(command)")
            try process.run()
            
            // Run asynchronously - don't block waiting for completion
            // Log output when process completes
            DispatchQueue.global(qos: .utility).async { [weak self] in
                process.waitUntilExit()
                
                let stdoutData = stdoutPipe.fileHandleForReading.readDataToEndOfFile()
                let stderrData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
                let stdoutStr = String(data: stdoutData, encoding: .utf8) ?? ""
                let stderrStr = String(data: stderrData, encoding: .utf8) ?? ""
                
                if process.terminationStatus != 0 {
                    self?.log("Wine reg command failed with exit code: \(process.terminationStatus)")
                    if !stdoutStr.isEmpty {
                        self?.log("stdout: \(stdoutStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                    }
                    if !stderrStr.isEmpty {
                        self?.log("stderr: \(stderrStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                    }
                } else {
                    if !stdoutStr.isEmpty {
                        self?.log("stdout: \(stdoutStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                    }
                    self?.log("Wine reg command completed successfully")
                }
            }
        } catch {
            log("Failed to run wine command: \(error)")
        }
    }
    
    // MARK: - GUID Conversion
    
    /// Convert standard GUID string to Wine REG_BINARY hex format
    private func guidToHexString(_ guid: String) -> String {
        let clean = guid.replacingOccurrences(of: "-", with: "").uppercased()
        
        // Data1 (8 chars) - reverse byte order
        let data1 = String(clean.prefix(8))
        let data1Reversed = stride(from: 6, through: 0, by: -2).map {
            let start = data1.index(data1.startIndex, offsetBy: $0)
            let end = data1.index(start, offsetBy: 2)
            return String(data1[start..<end])
        }.joined()
        
        // Data2 (4 chars) - reverse byte order
        let data2Start = clean.index(clean.startIndex, offsetBy: 8)
        let data2End = clean.index(data2Start, offsetBy: 4)
        let data2 = String(clean[data2Start..<data2End])
        let data2Reversed = String(data2.suffix(2)) + String(data2.prefix(2))
        
        // Data3 (4 chars) - reverse byte order
        let data3Start = clean.index(clean.startIndex, offsetBy: 12)
        let data3End = clean.index(data3Start, offsetBy: 4)
        let data3 = String(clean[data3Start..<data3End])
        let data3Reversed = String(data3.suffix(2)) + String(data3.prefix(2))
        
        // Data4 (16 chars) - keep as-is
        let data4 = String(clean.suffix(16))
        
        return data1Reversed + data2Reversed + data3Reversed + data4
    }
    
    /// Convert Wine REG_BINARY hex string back to standard GUID
    private func hexStringToGUID(_ hexData: String) -> String? {
        let clean = hexData.uppercased()
        guard clean.count == 32 else { return nil }
        
        // Data1 (8 chars) - reverse byte order back
        let data1Bytes = String(clean.prefix(8))
        let data1 = stride(from: 6, through: 0, by: -2).map {
            let start = data1Bytes.index(data1Bytes.startIndex, offsetBy: $0)
            let end = data1Bytes.index(start, offsetBy: 2)
            return String(data1Bytes[start..<end])
        }.joined()
        
        // Data2 (4 chars) - reverse byte order back
        let data2Start = clean.index(clean.startIndex, offsetBy: 8)
        let data2End = clean.index(data2Start, offsetBy: 4)
        let data2Bytes = String(clean[data2Start..<data2End])
        let data2 = String(data2Bytes.suffix(2)) + String(data2Bytes.prefix(2))
        
        // Data3 (4 chars) - reverse byte order back
        let data3Start = clean.index(clean.startIndex, offsetBy: 12)
        let data3End = clean.index(data3Start, offsetBy: 4)
        let data3Bytes = String(clean[data3Start..<data3End])
        let data3 = String(data3Bytes.suffix(2)) + String(data3Bytes.prefix(2))
        
        // Data4 (16 chars) - split into two parts
        let data4Start = clean.index(clean.startIndex, offsetBy: 16)
        let data4Part1End = clean.index(data4Start, offsetBy: 4)
        let data4Part1 = String(clean[data4Start..<data4Part1End])
        let data4Part2 = String(clean[data4Part1End...])
        
        return "\(data1)-\(data2)-\(data3)-\(data4Part1)-\(data4Part2)"
    }
    
    private func log(_ message: String) {
        let timestamp = ISO8601DateFormatter().string(from: Date())
        print("[\(timestamp)] [WineRegistry] \(message)")
        fflush(stdout)
    }
}
