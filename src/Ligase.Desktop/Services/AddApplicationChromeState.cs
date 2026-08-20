namespace Ligase.Host.Desktop.Services;

internal sealed class AddApplicationChromeState
{
    internal const double CollapseOffset = 96;
    internal const double ExpandOffset = 24;

    public bool IsCompact { get; private set; }

    public bool Update(double verticalOffset)
    {
        var next = IsCompact
            ? verticalOffset > ExpandOffset
            : verticalOffset >= CollapseOffset;
        if (next == IsCompact) return false;
        IsCompact = next;
        return true;
    }

    public bool Expand()
    {
        if (!IsCompact) return false;
        IsCompact = false;
        return true;
    }
}
