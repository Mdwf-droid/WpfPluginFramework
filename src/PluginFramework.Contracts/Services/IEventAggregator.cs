namespace PluginFramework.Contracts.Services;

public interface IEventAggregator
{
    void Publish<TEvent>(TEvent @event) where TEvent : class;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
