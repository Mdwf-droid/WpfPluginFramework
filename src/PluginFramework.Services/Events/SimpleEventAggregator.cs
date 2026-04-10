using PluginFramework.Contracts.Services;

namespace PluginFramework.Services.Events;

public class SimpleEventAggregator : IEventAggregator
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        List<Delegate> handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out handlers!))
                return;
            handlers = handlers.ToList(); // snapshot
        }

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler)(@event);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventAggregator handler error: {ex.Message}");
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_lock)
        {
            if (!_handlers.ContainsKey(typeof(TEvent)))
                _handlers[typeof(TEvent)] = new List<Delegate>();
            _handlers[typeof(TEvent)].Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                _handlers[typeof(TEvent)].Remove(handler);
            }
        });
    }

    private class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;
        public void Dispose() => _unsubscribe();
    }
}
