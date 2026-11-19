namespace Tor.Control;

[Flags]
public enum OnionDescriptorEvents { None = 0, Requested = 1, Received = 2, Failed = 4, Ignored = 8 }

public interface IOnionDescriptorDiagnostics
{
    Task<OnionDescriptorObservation> ObserveDescriptorAsync(OnionAddress address, CancellationToken token);
}

// The observer retains only the presence of allowlisted events for one address.
public sealed class OnionDescriptorObservation : IDisposable
{
    private readonly Action<OnionDescriptorObservation> close;
    private int events;

    internal OnionDescriptorObservation(OnionAddress address, Action<OnionDescriptorObservation> close)
    {
        Address = address;
        this.close = close;
    }

    internal OnionAddress Address { get; }
    public OnionDescriptorEvents Events => (OnionDescriptorEvents)Volatile.Read(ref events);
    internal void Record(OnionDescriptorEvents result) => Interlocked.Or(ref events, (int)result);
    public void Dispose() => close(this);
}
