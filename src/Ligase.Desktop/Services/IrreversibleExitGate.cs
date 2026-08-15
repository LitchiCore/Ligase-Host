namespace Ligase.Host.Desktop.Services;

internal sealed class IrreversibleExitGate
{
    private int _state;

    public bool IsCommitted => Volatile.Read(ref _state) != 0;

    public bool TryCommit() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
}
