using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace XIVTheCalamity.Helpers;

/// <summary>
/// IPC 進度事件的傳送委派。
/// </summary>
public delegate Task IpcEventSender(string eventType, JsonElement? data, CancellationToken ct);

public static class IpcEventHelper
{
    public static readonly object SenderKey = new();

    /// <summary>
    /// 標記此命令會持續傳送 IPC 事件。
    /// </summary>
    public static void SetupStream(HttpContext context)
    {
        context.Response.ContentType = "application/x-xivtc-events";
        var sender = (IpcEventSender)context.Items[SenderKey]!;
        _ = sender("open", null, context.RequestAborted);
    }

    /// <summary>
    /// 使用 NativeAOT 相容的 JSON 型別資訊傳送事件。
    /// </summary>
    public static Task SendAsync<T>(
        HttpResponse response,
        string eventType,
        T data,
        JsonTypeInfo<T> info,
        CancellationToken ct = default)
    {
        var sender = (IpcEventSender)response.HttpContext.Items[SenderKey]!;
        return sender(eventType, JsonSerializer.SerializeToElement(data, info), ct);
    }

    /// <summary>
    /// 依進度狀態決定事件類型。
    /// </summary>
    public static string DetermineEventType(bool hasError, bool isComplete)
    {
        if (hasError) return "error";
        if (isComplete) return "complete";
        return "progress";
    }
}
