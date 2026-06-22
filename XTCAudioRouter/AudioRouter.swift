import Foundation
import CoreAudio
import AudioToolbox
import Darwin

// 1. Logger
private let _isoFormatter = ISO8601DateFormatter()
func xtcLog(label: String, _ message: String) {
    let timestamp = _isoFormatter.string(from: Date())
    print("[\(timestamp)] [\(label)] \(message)")
    fflush(stdout)
}

// 2. WineRegistry
class WineRegistry {
    private let winePath: String
    private let winePrefix: String
    private let userRegPath: String
    private let msync: Bool
    private let wineDriverKey = #"HKEY_CURRENT_USER\Software\Wine\Drivers\winecoreaudio.drv"#
    private var rescanCounter: Int = 0
    
    init(winePath: String, winePrefix: String, msync: Bool = true) {
        self.winePath = winePath
        self.winePrefix = winePrefix
        self.userRegPath = (winePrefix as NSString).appendingPathComponent("user.reg")
        self.msync = msync
        xtcLog(label: "WineRegistry", "Initialized. Prefix: \(winePrefix), Wine: \(winePath)")
    }
    
    func setDefaultOutput(guid: String) {
        let deviceID = "{0.0.0.00000000}.{\(guid)}"
        xtcLog(label: "WineRegistry", "Preparing to queue DefaultOutput = \(deviceID)")
        runWineReg(key: wineDriverKey, value: "DefaultOutput", data: deviceID)
    }
    
    func rescanDevices() {
        rescanCounter = (rescanCounter == 0) ? 1 : 0
        xtcLog(label: "WineRegistry", "Preparing to queue RescanDevices with value \(rescanCounter)")
        runWineRegDword(key: wineDriverKey, value: "RescanDevices", data: rescanCounter)
    }
    
    func createDeviceMapping(coreAudioUID: String, guid: String) {
        let deviceKey = "\(wineDriverKey)\\devices\\0,\(coreAudioUID)"
        let hexData = guidToHexString(guid)
        xtcLog(label: "WineRegistry", "Writing device mapping (sync): \(coreAudioUID) -> \(guid)")
        let args = ["reg", "add", deviceKey, "/v", "guid", "/t", "REG_BINARY", "/d", hexData, "/f"]
        runWineCommandSync(args: args)
    }
    
    func readExistingGUID(for coreAudioUID: String) -> String? {
        xtcLog(label: "WineRegistry", "Reading existing GUID for \(coreAudioUID) from file...")
        guard let content = try? String(contentsOfFile: userRegPath, encoding: .utf8) else {
            xtcLog(label: "WineRegistry", "Warning: failed to read user.reg file at \(userRegPath)")
            return nil
        }
        
        let escapedUID = coreAudioUID
            .replacingOccurrences(of: "\\", with: "\\\\\\\\")
            .replacingOccurrences(of: ".", with: "\\.")
            .replacingOccurrences(of: "-", with: "\\-")
        let sectionPattern = "\\[Software\\\\\\\\Wine\\\\\\\\Drivers\\\\\\\\winecoreaudio\\.drv\\\\\\\\devices\\\\\\\\0,\(escapedUID)\\]"
        
        guard let sectionRange = content.range(of: sectionPattern, options: .regularExpression) else {
            xtcLog(label: "WineRegistry", "No existing file section found for \(coreAudioUID)")
            return nil
        }
        
        let sectionStart = sectionRange.upperBound
        let remainingContent = String(content[sectionStart...])
        
        let nextSectionRange = remainingContent.range(of: "\n[", options: [])
        let sectionContent: String
        if let nextRange = nextSectionRange {
            sectionContent = String(remainingContent[..<nextRange.lowerBound])
        } else {
            sectionContent = remainingContent
        }
        
        let guidPattern = #""guid"=hex:([0-9a-fA-F,]+)"#
        guard let guidMatch = sectionContent.range(of: guidPattern, options: .regularExpression) else {
            xtcLog(label: "WineRegistry", "No GUID value found in section for \(coreAudioUID)")
            return nil
        }
        
        let matchedString = String(sectionContent[guidMatch])
        guard let hexStart = matchedString.range(of: "hex:")?.upperBound else {
            return nil
        }
        let hexData = String(matchedString[hexStart...]).replacingOccurrences(of: ",", with: "")
        
        let guid = hexStringToGUID(hexData)
        xtcLog(label: "WineRegistry", "Parsed existing GUID for \(coreAudioUID) from user.reg: \(guid ?? "nil")")
        return guid
    }
    
    private func runWineReg(key: String, value: String, data: String) {
        let args = ["reg", "add", key, "/v", value, "/d", data, "/f"]
        runWineCommand(args: args)
    }
    
    private func runWineRegDword(key: String, value: String, data: Int) {
        let args = ["reg", "add", key, "/v", value, "/t", "REG_DWORD", "/d", String(data), "/f"]
        runWineCommand(args: args)
    }
    
    private func runWineCommandSync(args: [String]) {
        let commandString = "\(self.winePath) \(args.joined(separator: " "))"
        xtcLog(label: "WineRegistry", "[EXEC] Starting process (sync): \(commandString)")
        
        let process = Process()
        process.executableURL = URL(fileURLWithPath: self.winePath)
        process.arguments = args
        
        var env = ProcessInfo.processInfo.environment
        env["WINEPREFIX"] = self.winePrefix
        env["WINEDEBUG"] = "-all"
        env["WINEMSYNC"] = self.msync ? "1" : "0"
        process.environment = env
        
        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()
        process.standardOutput = stdoutPipe
        process.standardError = stderrPipe
        
        do {
            try process.run()
            process.waitUntilExit()
            
            let stdoutData = stdoutPipe.fileHandleForReading.readDataToEndOfFile()
            let stderrData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
            let stdoutStr = String(data: stdoutData, encoding: .utf8) ?? ""
            let stderrStr = String(data: stderrData, encoding: .utf8) ?? ""
            
            if process.terminationStatus != 0 {
                xtcLog(label: "WineRegistry", "[ERROR] Wine command failed. Exit code: \(process.terminationStatus)")
                if !stdoutStr.isEmpty {
                    xtcLog(label: "WineRegistry", "[ERROR] stdout: \(stdoutStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                }
                if !stderrStr.isEmpty {
                    xtcLog(label: "WineRegistry", "[ERROR] stderr: \(stderrStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                }
            } else {
                xtcLog(label: "WineRegistry", "[SUCCESS] Command completed successfully: \(commandString)")
            }
        } catch {
            xtcLog(label: "WineRegistry", "[CRITICAL] Failed to run wine command: \(error)")
        }
    }
    
    private func runWineCommand(args: [String]) {
        let commandString = "\(self.winePath) \(args.joined(separator: " "))"
        xtcLog(label: "WineRegistry", "[EXEC] Starting process (async): \(commandString)")
        
        let process = Process()
        process.executableURL = URL(fileURLWithPath: self.winePath)
        process.arguments = args
        
        var env = ProcessInfo.processInfo.environment
        env["WINEPREFIX"] = self.winePrefix
        env["WINEDEBUG"] = "-all"
        env["WINEMSYNC"] = self.msync ? "1" : "0"
        process.environment = env
        
        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()
        process.standardOutput = stdoutPipe
        process.standardError = stderrPipe
        
        do {
            try process.run()
            
            // Wait for exit in background thread to avoid blocking
            DispatchQueue.global(qos: .utility).async {
                process.waitUntilExit()
                
                let stdoutData = stdoutPipe.fileHandleForReading.readDataToEndOfFile()
                let stderrData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
                let stdoutStr = String(data: stdoutData, encoding: .utf8) ?? ""
                let stderrStr = String(data: stderrData, encoding: .utf8) ?? ""
                
                if process.terminationStatus != 0 {
                    xtcLog(label: "WineRegistry", "[ERROR] Wine command failed. Exit code: \(process.terminationStatus)")
                    if !stdoutStr.isEmpty {
                        xtcLog(label: "WineRegistry", "[ERROR] stdout: \(stdoutStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                    }
                    if !stderrStr.isEmpty {
                        xtcLog(label: "WineRegistry", "[ERROR] stderr: \(stderrStr.trimmingCharacters(in: .whitespacesAndNewlines))")
                    }
                } else {
                    xtcLog(label: "WineRegistry", "[SUCCESS] Command completed successfully: \(commandString)")
                }
            }
        } catch {
            xtcLog(label: "WineRegistry", "[CRITICAL] Failed to run wine command: \(error)")
        }
    }
    
    private func guidToHexString(_ guid: String) -> String {
        let clean = guid.replacingOccurrences(of: "-", with: "").uppercased()
        
        let data1 = String(clean.prefix(8))
        let data1Reversed = stride(from: 6, through: 0, by: -2).map {
            let start = data1.index(data1.startIndex, offsetBy: $0)
            let end = data1.index(start, offsetBy: 2)
            return String(data1[start..<end])
        }.joined()
        
        let data2Start = clean.index(clean.startIndex, offsetBy: 8)
        let data2End = clean.index(data2Start, offsetBy: 4)
        let data2 = String(clean[data2Start..<data2End])
        let data2Reversed = String(data2.suffix(2)) + String(data2.prefix(2))
        
        let data3Start = clean.index(clean.startIndex, offsetBy: 12)
        let data3End = clean.index(data3Start, offsetBy: 4)
        let data3 = String(clean[data3Start..<data3End])
        let data3Reversed = String(data3.suffix(2)) + String(data3.prefix(2))
        
        let data4 = String(clean.suffix(16))
        
        return data1Reversed + data2Reversed + data3Reversed + data4
    }
    
    private func hexStringToGUID(_ hexData: String) -> String? {
        let clean = hexData.uppercased()
        guard clean.count == 32 else { return nil }
        
        let data1Bytes = String(clean.prefix(8))
        let data1 = stride(from: 6, through: 0, by: -2).map {
            let start = data1Bytes.index(data1Bytes.startIndex, offsetBy: $0)
            let end = data1Bytes.index(start, offsetBy: 2)
            return String(data1Bytes[start..<end])
        }.joined()
        
        let data2Start = clean.index(clean.startIndex, offsetBy: 8)
        let data2End = clean.index(data2Start, offsetBy: 4)
        let data2Bytes = String(clean[data2Start..<data2End])
        let data2 = String(data2Bytes.suffix(2)) + String(data2Bytes.prefix(2))
        
        let data3Start = clean.index(clean.startIndex, offsetBy: 12)
        let data3End = clean.index(data3Start, offsetBy: 4)
        let data3Bytes = String(clean[data3Start..<data3End])
        let data3 = String(data3Bytes.suffix(2)) + String(data3Bytes.prefix(2))
        
        let data4Start = clean.index(clean.startIndex, offsetBy: 16)
        let data4Part1End = clean.index(data4Start, offsetBy: 4)
        let data4Part1 = String(clean[data4Start..<data4Part1End])
        let data4Part2 = String(clean[data4Part1End...])
        
        return "\(data1)-\(data2)-\(data3)-\(data4Part1)-\(data4Part2)"
    }
}

// 3. AudioDeviceManager
enum AudioDeviceManager {
    static func getDefaultOutputDevice() -> AudioDeviceID {
        var deviceID = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        _ = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &deviceID)
        return deviceID
    }
    
    static func getDeviceUID(deviceID: AudioDeviceID) -> String? {
        var uid: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceUID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        let status = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &uid)
        if status != noErr { return nil }
        return uid as String?
    }
    
    static func getDeviceName(deviceID: AudioDeviceID) -> String? {
        var name: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceNameCFString,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        let status = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &name)
        if status != noErr { return nil }
        return name as String?
    }
    
    static func getAllOutputDeviceUIDs() -> [String] {
        var size: UInt32 = 0
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var status = AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size)
        if status != noErr { return [] }
        
        let deviceCount = Int(size) / MemoryLayout<AudioDeviceID>.size
        var deviceIDs = [AudioDeviceID](repeating: 0, count: deviceCount)
        status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &deviceIDs)
        if status != noErr { return [] }
        
        var outputUIDs: [String] = []
        for deviceID in deviceIDs {
            var streamAddress = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyStreams,
                mScope: kAudioDevicePropertyScopeOutput,
                mElement: kAudioObjectPropertyElementMain
            )
            var streamSize: UInt32 = 0
            let streamStatus = AudioObjectGetPropertyDataSize(deviceID, &streamAddress, 0, nil, &streamSize)
            if streamStatus == noErr && streamSize > 0 {
                if let uid = getDeviceUID(deviceID: deviceID) {
                    outputUIDs.append(uid)
                }
            }
        }
        return outputUIDs
    }
    
    private static var defaultOutputListenerCallback: ((AudioDeviceID) -> Void)?
    private static var defaultOutputListenerBlock: AudioObjectPropertyListenerBlock?
    private static var devicesListenerCallback: (([String]) -> Void)?
    private static var devicesListenerBlock: AudioObjectPropertyListenerBlock?
    
    static func registerDefaultOutputListener(callback: @escaping (AudioDeviceID) -> Void) {
        defaultOutputListenerCallback = callback
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        defaultOutputListenerBlock = { (_, _) in
            let newDeviceID = getDefaultOutputDevice()
            DispatchQueue.main.async {
                defaultOutputListenerCallback?(newDeviceID)
            }
        }
        _ = AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, defaultOutputListenerBlock!)
    }
    
    static func removeDefaultOutputListener() {
        guard let block = defaultOutputListenerBlock else { return }
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        _ = AudioObjectRemovePropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
        defaultOutputListenerBlock = nil
        defaultOutputListenerCallback = nil
    }
    
    static func registerDevicesListener(callback: @escaping ([String]) -> Void) {
        devicesListenerCallback = callback
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        devicesListenerBlock = { (_, _) in
            let currentUIDs = getAllOutputDeviceUIDs()
            DispatchQueue.main.async {
                devicesListenerCallback?(currentUIDs)
            }
        }
        _ = AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, devicesListenerBlock!)
    }
    
    static func removeDevicesListener() {
        guard let block = devicesListenerBlock else { return }
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        _ = AudioObjectRemovePropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
        devicesListenerBlock = nil
        devicesListenerCallback = nil
    }
}

// 4. AudioRouter
class AudioRouter {
    private let wineRegistry: WineRegistry
    private(set) var currentOutputDeviceUID: String = ""
    private(set) var currentWineGUID: String = ""
    private var deviceGUIDCache: [String: String] = [:]
    private var knownDeviceUIDs: Set<String> = []
    private(set) var isRunning: Bool = false
    private var isFirstRoute: Bool = true
    
    private let workQueue = DispatchQueue(label: "com.xivthecalamity.xtcaudiorouter.workqueue")
    private var pendingRouteWorkItem: DispatchWorkItem?
    
    init(wineRegistry: WineRegistry) {
        self.wineRegistry = wineRegistry
        xtcLog(label: "AudioRouter", "Initialized.")
    }
    
    @discardableResult
    func start() -> Bool {
        xtcLog(label: "AudioRouter", "start() called. Current run state isRunning=\(isRunning)")
        guard !isRunning else { return true }
        
        // Record current known devices and default device UID on start
        knownDeviceUIDs = Set(AudioDeviceManager.getAllOutputDeviceUIDs())
        let defaultDevice = AudioDeviceManager.getDefaultOutputDevice()
        if let uid = AudioDeviceManager.getDeviceUID(deviceID: defaultDevice) {
            currentOutputDeviceUID = uid
        }
        
        xtcLog(label: "AudioRouter", "Registering default output and devices list listeners...")
        AudioDeviceManager.registerDefaultOutputListener { [weak self] newDeviceID in
            self?.onDefaultOutputChanged(newDeviceID: newDeviceID)
        }
        AudioDeviceManager.registerDevicesListener { [weak self] currentUIDs in
            self?.onDeviceListChanged(currentUIDs: currentUIDs)
        }
        
        isRunning = true
        xtcLog(label: "AudioRouter", "Listeners registered successfully. Triggering initial performRoutingUpdate().")
        
        // Trigger initial routing update immediately
        performRoutingUpdate()
        
        return true
    }
    
    func stop() {
        xtcLog(label: "AudioRouter", "stop() called. Current run state isRunning=\(isRunning)")
        guard isRunning else { return }
        
        if pendingRouteWorkItem != nil {
            xtcLog(label: "AudioRouter", "Cancelling pending routing work item.")
            pendingRouteWorkItem?.cancel()
            pendingRouteWorkItem = nil
        }
        
        xtcLog(label: "AudioRouter", "Removing listeners...")
        AudioDeviceManager.removeDefaultOutputListener()
        AudioDeviceManager.removeDevicesListener()
        isRunning = false
        knownDeviceUIDs.removeAll()
        xtcLog(label: "AudioRouter", "Audio routing stopped.")
    }
    
    func forceRescan() {
        xtcLog(label: "AudioRouter", "forceRescan() manually triggered by ProcessMonitor.")
        wineRegistry.rescanDevices()
    }
    
    private func queueRoutingUpdate() {
        xtcLog(label: "AudioRouter", "queueRoutingUpdate() requested. Checking for pending tasks...")
        if pendingRouteWorkItem != nil {
            xtcLog(label: "AudioRouter", "Cancelling previous queued update.")
            pendingRouteWorkItem?.cancel()
        }
        
        let workItem = DispatchWorkItem { [weak self] in
            xtcLog(label: "AudioRouter", "[QUEUE] Running scheduled performRoutingUpdate now.")
            self?.performRoutingUpdate()
        }
        
        pendingRouteWorkItem = workItem
        xtcLog(label: "AudioRouter", "Queued new routing update to run in 800ms.")
        workQueue.asyncAfter(deadline: .now() + 0.8, execute: workItem)
    }
    
    private func performRoutingUpdate() {
        xtcLog(label: "AudioRouter", "performRoutingUpdate() - Starting synchronization...")
        
        let defaultDevice = AudioDeviceManager.getDefaultOutputDevice()
        guard let uid = AudioDeviceManager.getDeviceUID(deviceID: defaultDevice) else {
            xtcLog(label: "AudioRouter", "[ERROR] performRoutingUpdate: Failed to get default output device UID")
            return
        }
        
        let allDeviceUIDs = AudioDeviceManager.getAllOutputDeviceUIDs()
        let deviceName = AudioDeviceManager.getDeviceName(deviceID: defaultDevice) ?? "Unknown"
        xtcLog(label: "AudioRouter", "[SYNC] Current macOS Default Device Name: '\(deviceName)', UID: '\(uid)'")
        xtcLog(label: "AudioRouter", "[SYNC] All available output device UIDs: \(allDeviceUIDs)")
        
        // Update known devices set
        knownDeviceUIDs = Set(allDeviceUIDs)
        
        let isFirst = isFirstRoute
        if isFirst {
            isFirstRoute = false
            xtcLog(label: "AudioRouter", "[SYNC] Starting first-time routing sequence for device UID: \(uid)")
            
            // Step 1: Rescan first
            wineRegistry.rescanDevices()
            
            // Step 2: Set default device after 800ms
            workQueue.asyncAfter(deadline: .now() + 0.8) { [weak self] in
                guard let self = self else { return }
                let guid = self.getOrCreateWineGUID(for: uid)
                self.wineRegistry.setDefaultOutput(guid: guid)
                self.currentOutputDeviceUID = uid
                self.currentWineGUID = guid
                
                // Step 3: Rescan again after 800ms
                self.workQueue.asyncAfter(deadline: .now() + 0.8) { [weak self] in
                    guard let self = self else { return }
                    self.wineRegistry.rescanDevices()
                    
                    // Step 4: Set default device again after 800ms
                    self.workQueue.asyncAfter(deadline: .now() + 0.8) { [weak self] in
                        guard let self = self else { return }
                        let finalGuid = self.getOrCreateWineGUID(for: uid)
                        self.wineRegistry.setDefaultOutput(guid: finalGuid)
                        xtcLog(label: "AudioRouter", "[SYNC] First-time routing sequence complete.")
                    }
                }
            }
        } else {
            xtcLog(label: "AudioRouter", "[SYNC] Starting standard routing sequence for device UID: \(uid)")
            
            // Step 1: Rescan first
            wineRegistry.rescanDevices()
            
            // Step 2: Set default device after 800ms
            workQueue.asyncAfter(deadline: .now() + 0.8) { [weak self] in
                guard let self = self else { return }
                let guid = self.getOrCreateWineGUID(for: uid)
                self.wineRegistry.setDefaultOutput(guid: guid)
                self.currentOutputDeviceUID = uid
                self.currentWineGUID = guid
                xtcLog(label: "AudioRouter", "[SYNC] Standard routing sequence complete.")
            }
        }
    }
    
    private func getOrCreateWineGUID(for coreAudioUID: String) -> String {
        if let cachedGUID = deviceGUIDCache[coreAudioUID] {
            xtcLog(label: "AudioRouter", "getOrCreateWineGUID: Cache HIT for \(coreAudioUID) -> \(cachedGUID)")
            return cachedGUID
        }
        
        xtcLog(label: "AudioRouter", "getOrCreateWineGUID: Cache MISS for \(coreAudioUID). Querying user.reg file...")
        if let existingGUID = wineRegistry.readExistingGUID(for: coreAudioUID) {
            xtcLog(label: "AudioRouter", "getOrCreateWineGUID: Found existing GUID in file: \(coreAudioUID) -> \(existingGUID)")
            deviceGUIDCache[coreAudioUID] = existingGUID
            return existingGUID
        }
        
        let guid = UUID().uuidString.uppercased()
        xtcLog(label: "AudioRouter", "getOrCreateWineGUID: No existing GUID. Generated new GUID: \(coreAudioUID) -> \(guid)")
        deviceGUIDCache[coreAudioUID] = guid
        wineRegistry.createDeviceMapping(coreAudioUID: coreAudioUID, guid: guid)
        return guid
    }
    
    private func onDeviceListChanged(currentUIDs: [String]) {
        let currentSet = Set(currentUIDs)
        let newDevices = currentSet.subtracting(knownDeviceUIDs)
        if !newDevices.isEmpty {
            xtcLog(label: "AudioRouter", "[EVENT] New device(s) detected: \(newDevices). Triggering Wine rescan.")
            wineRegistry.rescanDevices()
        }
        knownDeviceUIDs = currentSet
    }
    
    private func onDefaultOutputChanged(newDeviceID: AudioDeviceID) {
        if let newUID = AudioDeviceManager.getDeviceUID(deviceID: newDeviceID) {
            let name = AudioDeviceManager.getDeviceName(deviceID: newDeviceID) ?? "Unknown"
            xtcLog(label: "AudioRouter", "[EVENT] Default Output changed in macOS: \(name) (\(newUID))")
            queueRoutingUpdate()
        } else {
            xtcLog(label: "AudioRouter", "[EVENT] Default Output changed in macOS, but failed to retrieve device UID.")
        }
    }
}

// 5. ProcessMonitor
class ProcessMonitor {
    private let targetPID: Int32
    private var timer: DispatchSourceTimer?
    private var onProcessExit: (() -> Void)?
    private var onProcessDetected: (() -> Void)?
    private var hasDetectedProcess: Bool = false
    private let checkInterval: TimeInterval = 2.0
    
    init(pid: Int32) {
        self.targetPID = pid
    }
    
    func start(onDetected: (() -> Void)? = nil, onExit: @escaping () -> Void) {
        self.onProcessDetected = onDetected
        self.onProcessExit = onExit
        
        guard kill(targetPID, 0) == 0 else {
            xtcLog(label: "ProcessMonitor", "Process \(targetPID) not found, exiting immediately")
            onExit()
            return
        }
        
        xtcLog(label: "ProcessMonitor", "Started monitoring PID \(targetPID)")
        if let onDetected = self.onProcessDetected {
            hasDetectedProcess = true
            DispatchQueue.main.async {
                onDetected()
            }
        }
        
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
        timer.schedule(deadline: .now() + checkInterval, repeating: checkInterval)
        timer.setEventHandler { [weak self] in
            guard let self = self else { return }
            if kill(self.targetPID, 0) != 0 {
                xtcLog(label: "ProcessMonitor", "Process \(self.targetPID) has exited")
                self.timer?.cancel()
                self.timer = nil
                DispatchQueue.main.async {
                    self.onProcessExit?()
                }
            }
        }
        timer.resume()
        self.timer = timer
    }
    
    func stop() {
        timer?.cancel()
        timer = nil
        xtcLog(label: "ProcessMonitor", "Stopped monitoring")
    }
}

// 6. CLI Entry Point
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

xtcLog(label: "XTCAudioRouter", "XTCAudioRouter starting...")
xtcLog(label: "XTCAudioRouter", "PID: \(opts.pid)")
xtcLog(label: "XTCAudioRouter", "Wine Prefix: \(opts.wineprefix)")
xtcLog(label: "XTCAudioRouter", "Wine: \(opts.wine)")
xtcLog(label: "XTCAudioRouter", "Msync: \(opts.msync)")

guard FileManager.default.fileExists(atPath: opts.wineprefix) else {
    xtcLog(label: "XTCAudioRouter", "Error: Wine prefix path does not exist: \(opts.wineprefix)")
    exit(1)
}
guard FileManager.default.fileExists(atPath: opts.wine) else {
    xtcLog(label: "XTCAudioRouter", "Error: Wine executable does not exist: \(opts.wine)")
    exit(1)
}

let wineRegistry = WineRegistry(winePath: opts.wine, winePrefix: opts.wineprefix, msync: opts.msync)
let audioRouter = AudioRouter(wineRegistry: wineRegistry)
let processMonitor = ProcessMonitor(pid: opts.pid)

guard audioRouter.start() else {
    xtcLog(label: "XTCAudioRouter", "Error: Failed to start audio routing")
    exit(1)
}

func shutdown() {
    audioRouter.stop()
    processMonitor.stop()
    Darwin.exit(0)
}

signal(SIGTERM) { _ in shutdown() }
signal(SIGINT) { _ in shutdown() }

processMonitor.start(
    onDetected: {
        xtcLog(label: "XTCAudioRouter", "Game process detected, triggering initial audio device rescan...")
        audioRouter.forceRescan()
    },
    onExit: {
        xtcLog(label: "XTCAudioRouter", "Game process exited, shutting down...")
        shutdown()
    }
)

xtcLog(label: "XTCAudioRouter", "Audio routing active. Monitoring PID \(opts.pid)...")
RunLoop.main.run()
