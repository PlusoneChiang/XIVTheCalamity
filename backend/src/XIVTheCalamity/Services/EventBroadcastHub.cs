using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace XIVTheCalamity.Services;

public class EventBroadcastHub
{
    private readonly ConcurrentDictionary<Guid, Channel<EventStreamItem>> _subscribers = new();

    public void Broadcast(string eventName, JsonElement data)
    {
        var item = new EventStreamItem(eventName, data);
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(item);
        }
    }

    public Guid Subscribe(Channel<EventStreamItem> channel)
    {
        var id = Guid.NewGuid();
        _subscribers.TryAdd(id, channel);
        return id;
    }

    public void Unsubscribe(Guid id)
    {
        _subscribers.TryRemove(id, out _);
    }
}
