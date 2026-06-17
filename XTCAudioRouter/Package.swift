// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "XTCAudioRouter",
    platforms: [
        .macOS(.v12)
    ],
    targets: [
        .executableTarget(
            name: "XTCAudioRouter",
            linkerSettings: [
                .linkedFramework("CoreAudio"),
                .linkedFramework("AudioToolbox")
            ]
        )
    ]
)
