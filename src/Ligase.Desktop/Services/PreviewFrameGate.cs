namespace Ligase.Host.Desktop.Services;

internal readonly record struct PreviewFrameLease(
    long Generation,
    string? DeviceName);

internal sealed class PreviewFrameGate
{
    private readonly object _sync = new();
    private long _generation;
    private bool _active;
    private string? _deviceName;

    public void Start(string? deviceName)
    {
        lock (_sync)
        {
            _active = true;
            _deviceName = deviceName;
            _generation++;
        }
    }

    public void ChangeSource(string? deviceName)
    {
        lock (_sync)
        {
            _deviceName = deviceName;
            _generation++;
        }
    }

    public PreviewFrameLease Capture()
    {
        lock (_sync) return new PreviewFrameLease(_generation, _deviceName);
    }

    public bool CanPublish(PreviewFrameLease lease)
    {
        lock (_sync)
            return _active && lease.Generation == _generation &&
                string.Equals(lease.DeviceName, _deviceName,
                    StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        lock (_sync)
        {
            _active = false;
            _generation++;
        }
    }
}
