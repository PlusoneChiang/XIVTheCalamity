using System.Text.Json.Serialization;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Models.Progress;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Api.NativeAOT.DTOs;

namespace XIVTheCalamity.Api.NativeAOT;

// Core Models
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(GameConfig))]
[JsonSerializable(typeof(DalamudConfig))]
[JsonSerializable(typeof(LauncherConfig))]
[JsonSerializable(typeof(WineConfig))]
[JsonSerializable(typeof(WineXIVConfig))]
[JsonSerializable(typeof(LoginResult))]
[JsonSerializable(typeof(WineInitProgress))]

// Progress Events
[JsonSerializable(typeof(DownloadProgressEvent))]
[JsonSerializable(typeof(PatchProgressEvent))]
[JsonSerializable(typeof(EnvironmentProgressEvent))]

// Dalamud Models
[JsonSerializable(typeof(DalamudVersionInfo))]
[JsonSerializable(typeof(DalamudAssetManifest))]
[JsonSerializable(typeof(DalamudAssetEntry))]
[JsonSerializable(typeof(DalamudStatus))]
[JsonSerializable(typeof(DalamudUpdateProgress))]

// API DTOs
[JsonSerializable(typeof(LoginRequestDto))]
[JsonSerializable(typeof(LoginResponseDto))]
[JsonSerializable(typeof(LoginData))]
[JsonSerializable(typeof(LaunchRequest))]
[JsonSerializable(typeof(CheckUpdateRequest))]

// API Response Wrappers
[JsonSerializable(typeof(ApiResponse<AppConfig>))]
[JsonSerializable(typeof(ApiResponse<LoginData>))]
[JsonSerializable(typeof(ApiResponse<LoginResponseDto>))]
[JsonSerializable(typeof(ApiResponse<string>))]
[JsonSerializable(typeof(ApiResponse<object>))]
[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(ErrorDetails))]

// Common Types
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]

// Health Check
[JsonSerializable(typeof(HealthResponse))]

// Game Status
[JsonSerializable(typeof(GameStatusResponse))]
[JsonSerializable(typeof(PathsResponse))]
[JsonSerializable(typeof(ConfigPathResponse))]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(UpdateCheckResponse))]
[JsonSerializable(typeof(DalamudStatusResponse))]

// SSE Event Types
[JsonSerializable(typeof(SseMessage))]
[JsonSerializable(typeof(SseError))]
[JsonSerializable(typeof(ToolLaunchResult))]

// Wine Response Types
[JsonSerializable(typeof(WineToolLaunchResponse))]
[JsonSerializable(typeof(WineSettingsAppliedResponse))]
[JsonSerializable(typeof(ApiResponse<WineToolLaunchResponse>))]
[JsonSerializable(typeof(ApiResponse<WineSettingsAppliedResponse>))]

// Game Response Types
[JsonSerializable(typeof(GameLaunchResponse))]
[JsonSerializable(typeof(GameExitResponse))]
[JsonSerializable(typeof(ApiResponse<GameLaunchResponse>))]
[JsonSerializable(typeof(ApiResponse<GameExitResponse>))]
[JsonSerializable(typeof(ApiResponse<GameStatusResponse>))]

// Dalamud Response Types
[JsonSerializable(typeof(ApiResponse<DalamudStatus>))]

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class AppJsonContext : JsonSerializerContext { }

// Simple response types for NativeAOT
public record HealthResponse(string Status, DateTime Timestamp);
public record GameStatusResponse(bool IsRunning, int? ProcessId);
public record PathsResponse(string WinePath, string WinePrefixPath, string GamePath);
public record ConfigPathResponse(string Path);
public record VersionResponse(string? BootVersion, string? GameVersion);
public record UpdateCheckResponse(bool HasUpdates, string? BootVersion, string? GameVersion, int PatchCount);
public record DalamudStatusResponse(DalamudStatus Status, string? Version);

// SSE specific types
public record SseMessage(string Message);
public record SseError(string Code, string Message);
public record ToolLaunchResult(bool Success, int ExitCode, string Stdout, string Stderr);

// Wine response types (NativeAOT compatible)
public record WineToolLaunchResponse(bool Success, string Message, int? Pid = null);
public record WineSettingsAppliedResponse(bool Success, string Message);

// Game response types (NativeAOT compatible)
public record GameLaunchResponse(int ProcessId, int? ExitCode = null);
public record GameExitResponse(int ExitCode);
