namespace PluginFramework.Resilience;

public class CircuitBreakerState
{
    public int FailureCount { get; set; }
    public DateTime LastFailure { get; set; }
    public List<Exception> RecentExceptions { get; set; } = new();

    public void RecordFailure(Exception ex)
    {
        FailureCount++;
        LastFailure = DateTime.UtcNow;
        RecentExceptions.Add(ex);
        if (RecentExceptions.Count > 10) RecentExceptions.RemoveAt(0);
    }

    public void Reset()
    {
        FailureCount = 0;
        RecentExceptions.Clear();
    }
}
