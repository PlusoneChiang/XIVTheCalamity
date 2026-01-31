//
//  AudioRouter.swift
//  XTCAudioRouter
//
//  Main audio routing logic
//  Based on XIV-on-Mac GameAudioRouter
//

import CoreAudio
import Foundation

/// Game audio router manager
class AudioRouter {
    
    private let wineRegistry: WineRegistry
    
    /// Current output device UID
    private(set) var currentOutputDeviceUID: String = ""
    
    /// Current output device Wine GUID
    private(set) var currentWineGUID: String = ""
    
    /// Device GUID cache (CoreAudio UID -> Wine GUID)
    private var deviceGUIDCache: [String: String] = [:]
    
    /// Known device UIDs (for detecting new device connections)
    private var knownDeviceUIDs: Set<String> = []
    
    /// Is running flag
    private(set) var isRunning: Bool = false
    
    init(wineRegistry: WineRegistry) {
        self.wineRegistry = wineRegistry
    }
    
    // MARK: - Public Methods
    
    /// Start audio routing
    /// - Returns: Whether started successfully
    @discardableResult
    func start() -> Bool {
        guard !isRunning else { return true }
        
        // 1. Get current system default device
        let defaultDevice = AudioDeviceManager.getDefaultOutputDevice()
        guard let uid = AudioDeviceManager.getDeviceUID(deviceID: defaultDevice) else {
            log("Failed to get default output device")
            return false
        }
        currentOutputDeviceUID = uid
        
        // 2. Get or create Wine GUID for this device
        let guid = getOrCreateWineGUID(for: uid)
        currentWineGUID = guid
        
        // 3. Set Wine default output device
        wineRegistry.setDefaultOutput(guid: guid)
        
        // 4. Record current known devices
        knownDeviceUIDs = Set(AudioDeviceManager.getAllOutputDeviceUIDs())
        
        // 5. Register device change listeners
        registerDeviceChangeListener()
        registerDeviceListChangeListener()
        
        isRunning = true
        let deviceName = AudioDeviceManager.getDeviceName(deviceID: defaultDevice) ?? "Unknown"
        log("Audio routing started, output: \(deviceName)")
        return true
    }
    
    /// Stop audio routing
    func stop() {
        guard isRunning else { return }
        
        AudioDeviceManager.removeDefaultOutputListener()
        AudioDeviceManager.removeDevicesListener()
        
        isRunning = false
        knownDeviceUIDs.removeAll()
        log("Audio routing stopped")
    }
    
    /// Get current output device name
    func getCurrentOutputDeviceName() -> String? {
        let defaultDevice = AudioDeviceManager.getDefaultOutputDevice()
        return AudioDeviceManager.getDeviceName(deviceID: defaultDevice)
    }
    
    // MARK: - Private Methods
    
    /// Get or create Wine GUID for device
    /// - Parameter coreAudioUID: CoreAudio device UID
    /// - Returns: Wine GUID
    private func getOrCreateWineGUID(for coreAudioUID: String) -> String {
        // Check cache
        if let cachedGUID = deviceGUIDCache[coreAudioUID] {
            return cachedGUID
        }
        
        // Try to read existing GUID from Wine Registry
        if let existingGUID = wineRegistry.readExistingGUID(for: coreAudioUID) {
            deviceGUIDCache[coreAudioUID] = existingGUID
            return existingGUID
        }
        
        // Not found, generate new GUID
        let guid = UUID().uuidString.uppercased()
        deviceGUIDCache[coreAudioUID] = guid
        
        // Write to Wine Registry to create mapping
        wineRegistry.createDeviceMapping(coreAudioUID: coreAudioUID, guid: guid)
        
        return guid
    }
    
    /// Register device change listener
    private func registerDeviceChangeListener() {
        AudioDeviceManager.registerDefaultOutputListener { [weak self] newDeviceID in
            self?.onDefaultOutputChanged(newDeviceID: newDeviceID)
        }
    }
    
    /// Register device list change listener
    private func registerDeviceListChangeListener() {
        AudioDeviceManager.registerDevicesListener { [weak self] currentUIDs in
            self?.onDeviceListChanged(currentUIDs: currentUIDs)
        }
    }
    
    /// Called when device list changes
    private func onDeviceListChanged(currentUIDs: [String]) {
        let currentSet = Set(currentUIDs)
        let newDevices = currentSet.subtracting(knownDeviceUIDs)
        
        if !newDevices.isEmpty {
            log("New device(s) detected, triggering rescan")
            wineRegistry.rescanDevices()
        }
        
        knownDeviceUIDs = currentSet
    }
    
    /// Called when system default audio changes
    private func onDefaultOutputChanged(newDeviceID: AudioDeviceID) {
        guard let newUID = AudioDeviceManager.getDeviceUID(deviceID: newDeviceID) else { return }
        guard newUID != currentOutputDeviceUID else { return }
        
        let deviceName = AudioDeviceManager.getDeviceName(deviceID: newDeviceID) ?? "Unknown"
        log("Output device changed: \(deviceName)")
        
        currentOutputDeviceUID = newUID
        let guid = getOrCreateWineGUID(for: newUID)
        currentWineGUID = guid
        wineRegistry.setDefaultOutput(guid: guid)
        
        // Trigger rescan so the game picks up the new default output
        wineRegistry.rescanDevices()
    }
    
    private func log(_ message: String) {
        let timestamp = ISO8601DateFormatter().string(from: Date())
        print("[\(timestamp)] [AudioRouter] \(message)")
        fflush(stdout)
    }
}
