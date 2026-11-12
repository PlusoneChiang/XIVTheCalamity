using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using XIVTheCalamity.DTOs;
using XIVTheCalamity.Helpers;

namespace XIVTheCalamity.Services;

/// <summary>將 Photino 訊息派送至程式內的端點，不建立網路連線。</summary>
internal sealed class IpcBridge(
    IServiceProvider services,
    RequestDelegate dispatch,
    string token,
    Action<string> send) : IDisposable, IAsyncDisposable
{
    private static readonly HashSet<string> StreamPaths =
    [
        "/api/events/stream", "/api/environment/initialize",
        "/api/update/install", "/api/dalamud/update-stream"
    ];
    private readonly ConcurrentDictionary<(string Client, string Id), Operation> _operations = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _admissionLock = new();

    public bool IsStopping => _shutdown.IsCancellationRequested;

    public void Receive(string json)
    {
        if (_shutdown.IsCancellationRequested) return;
        IpcRequestMessage? message;
        try { message = JsonSerializer.Deserialize(json, AppJsonContext.Default.IpcRequestMessage); }
        catch (JsonException) { return; }
        if (message == null || !string.Equals(message.Token, token, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(message.ClientId) || message.ClientId.Length > 100 ||
            string.IsNullOrEmpty(message.Id) || message.Id.Length > 100)
            return;

        var key = (message.ClientId, message.Id);
        switch (message.Type)
        {
            case "ready":
                foreach (var running in _operations.Values) running.Cancel();
                _ = Task.Run(() => SendAsync(new("ready", message.Id, message.ClientId)));
                return;
            case "cancel":
                if (_operations.TryGetValue(key, out var canceled)) canceled.Cancel();
                return;
            case "release":
                foreach (var pair in _operations)
                    if (pair.Key.Client == message.ClientId) pair.Value.Cancel();
                return;
            case "ack":
                if (_operations.TryGetValue(key, out var acknowledged)) acknowledged.Acknowledged.TrySetResult();
                return;
            case "request":
            case "stream":
                lock (_admissionLock)
                {
                    if (_shutdown.IsCancellationRequested) return;
                    var operation = new Operation(_shutdown.Token);
                    if (!_operations.TryAdd(key, operation))
                    {
                        operation.Dispose();
                        return;
                    }
                    _ = Task.Run(() => DispatchAsync(message, operation));
                }
                return;
        }
    }

    private async Task DispatchAsync(IpcRequestMessage message, Operation operation)
    {
        var streaming = message.Type == "stream";
        var responseFeature = new CompletionFeature();
        var replied = false;
        try
        {
            if (message.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE") ||
                string.IsNullOrEmpty(message.Path) || !message.Path.StartsWith("/api/", StringComparison.Ordinal) ||
                !Uri.TryCreate("https://ipc.local" + message.Path, UriKind.Absolute, out var uri) ||
                !uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ||
                streaming != (message.Method == "GET" && StreamPaths.Contains(uri.AbsolutePath)))
                throw new BadHttpRequestException("不支援的 IPC 命令。", StatusCodes.Status400BadRequest);

            await using var scope = services.CreateAsyncScope();
            await using var output = new MemoryStream();
            var body = message.Body?.GetRawText();
            await using var input = new MemoryStream(body == null ? [] : Encoding.UTF8.GetBytes(body));
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(responseFeature);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature(body != null));
            context.RequestServices = scope.ServiceProvider;
            context.RequestAborted = operation.Cancellation.Token;
            context.Request.Method = message.Method;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("ipc.local");
            context.Request.Path = PathString.FromUriComponent(uri);
            context.Request.QueryString = new QueryString(uri.Query);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = input.Length;
            context.Request.Body = input;
            context.Response.Body = output;
            context.Items[IpcEventHelper.SenderKey] = (IpcEventSender)((eventType, data, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return SendAsync(new("event", message.Id, message.ClientId) { EventType = eventType, Data = data });
            });

            await dispatch(context);

            JsonElement? data = null;
            if (output.Length > 0)
            {
                output.Position = 0;
                using var document = await JsonDocument.ParseAsync(output);
                data = document.RootElement.Clone();
            }
            await SendAsync(new(streaming ? "end" : "response", message.Id, message.ClientId)
            {
                Status = context.Response.StatusCode,
                StatusText = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
                Data = data
            });
            replied = true;
            if (responseFeature.HasCallbacks)
            {
                // 關閉或重新啟動視窗前，先讓前端接到回覆。
                await operation.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5), _shutdown.Token);
                await responseFeature.CompleteAsync();
            }
        }
        catch (Exception ex)
        {
            if (replied) return;
            var status = ex is OperationCanceledException ? 499 :
                ex is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
            var error = ApiErrorResponse.Create(status == 499 ? "CANCELED" : "IPC_REQUEST_FAILED",
                status == 499 ? "作業已取消。" : status == 400 ? "IPC 要求格式不正確。" : "無法完成 IPC 要求。");
            await SendAsync(new(streaming ? "end" : "response", message.Id, message.ClientId)
            {
                Status = status,
                StatusText = ReasonPhrases.GetReasonPhrase(status),
                Data = JsonSerializer.SerializeToElement(error, AppJsonContext.Default.ApiErrorResponse)
            });
        }
        finally
        {
            operation.Dispose();
            operation.Finished.TrySetResult();
            _operations.TryRemove((message.ClientId, message.Id), out _);
        }
    }

    private Task SendAsync(IpcResponseMessage message)
    {
        if (!_shutdown.IsCancellationRequested)
            send(JsonSerializer.Serialize(message, AppJsonContext.Default.IpcResponseMessage));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_admissionLock)
        {
            if (_shutdown.IsCancellationRequested) return;
            _shutdown.Cancel();
            foreach (var running in _operations.Values) running.Cancel();
        }
    }

    public Task DrainAsync() => Task.WhenAll(_operations.Values.Select(operation => operation.Finished.Task));

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DrainAsync();
    }

    private sealed class Operation(CancellationToken shutdown) : IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        public TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class BodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => canHaveBody;
    }

    private sealed class CompletionFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];
        public bool HasCallbacks => _callbacks.Count > 0;
        public override void OnCompleted(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));
        public async Task CompleteAsync()
        {
            for (var i = _callbacks.Count - 1; i >= 0; i--)
                await _callbacks[i].Callback(_callbacks[i].State);
        }
    }
}

public sealed class IpcRequestMessage
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "";
    public JsonElement? Body { get; set; }
}

public sealed record IpcResponseMessage(string Type, string Id, string ClientId)
{
    public int? Status { get; init; }
    public string? StatusText { get; init; }
    public string? EventType { get; init; }
    public JsonElement? Data { get; init; }
}
