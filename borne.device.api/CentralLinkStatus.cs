namespace borne.device.api;

public sealed class CentralLinkStatus
{
    private readonly Lock gate = new();
    private bool isConnected;
    private DateTimeOffset? lastConnectedAtUtc;

    public bool IsConnected
    {
        get { lock (gate) return isConnected; }
    }

    public DateTimeOffset? LastConnectedAtUtc
    {
        get { lock (gate) return lastConnectedAtUtc; }
    }

    public void SetConnected(bool connected)
    {
        lock (gate)
        {
            isConnected = connected;
            if (connected) lastConnectedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
