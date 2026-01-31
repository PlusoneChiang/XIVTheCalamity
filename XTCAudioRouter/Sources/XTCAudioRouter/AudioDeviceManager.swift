//
//  AudioDeviceManager.swift
//  XTCAudioRouter
//
//  CoreAudio device management wrapper
//  Based on XIV-on-Mac implementation
//

import AudioToolbox
import CoreAudio
import Foundation

/// CoreAudio device management wrapper
enum AudioDeviceManager {
    
    // MARK: - Get Device Info
    
    /// Get current system default output device
    static func getDefaultOutputDevice() -> AudioDeviceID {
        var deviceID = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        _ = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            &size,
            &deviceID
        )
        
        return deviceID
    }
    
    /// Get device UID
    static func getDeviceUID(deviceID: AudioDeviceID) -> String? {
        var uid: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceUID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        let status = AudioObjectGetPropertyData(
            deviceID,
            &address,
            0,
            nil,
            &size,
            &uid
        )
        
        if status != noErr { return nil }
        return uid as String?
    }
    
    /// Get device name
    static func getDeviceName(deviceID: AudioDeviceID) -> String? {
        var name: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceNameCFString,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        let status = AudioObjectGetPropertyData(
            deviceID,
            &address,
            0,
            nil,
            &size,
            &name
        )
        
        if status != noErr { return nil }
        return name as String?
    }
    
    /// Get all audio output device UIDs
    static func getAllOutputDeviceUIDs() -> [String] {
        var size: UInt32 = 0
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        var status = AudioObjectGetPropertyDataSize(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            &size
        )
        
        if status != noErr { return [] }
        
        let deviceCount = Int(size) / MemoryLayout<AudioDeviceID>.size
        var deviceIDs = [AudioDeviceID](repeating: 0, count: deviceCount)
        
        status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            &size,
            &deviceIDs
        )
        
        if status != noErr { return [] }
        
        // Only return devices with output capability
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
    
    // MARK: - Device Change Listeners
    
    private static var defaultOutputListenerCallback: ((AudioDeviceID) -> Void)?
    private static var defaultOutputListenerBlock: AudioObjectPropertyListenerBlock?
    
    private static var devicesListenerCallback: (([String]) -> Void)?
    private static var devicesListenerBlock: AudioObjectPropertyListenerBlock?
    
    /// Register default output device change listener
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
        
        _ = AudioObjectAddPropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            DispatchQueue.main,
            defaultOutputListenerBlock!
        )
    }
    
    /// Remove default output device change listener
    static func removeDefaultOutputListener() {
        guard let block = defaultOutputListenerBlock else { return }
        
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        _ = AudioObjectRemovePropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            DispatchQueue.main,
            block
        )
        
        defaultOutputListenerBlock = nil
        defaultOutputListenerCallback = nil
    }
    
    /// Register device list change listener
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
        
        _ = AudioObjectAddPropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            DispatchQueue.main,
            devicesListenerBlock!
        )
    }
    
    /// Remove device list change listener
    static func removeDevicesListener() {
        guard let block = devicesListenerBlock else { return }
        
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        
        _ = AudioObjectRemovePropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            DispatchQueue.main,
            block
        )
        
        devicesListenerBlock = nil
        devicesListenerCallback = nil
    }
}
