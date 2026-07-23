using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface IApolloAppsWriter
{
    Task WriteAsync(IReadOnlyCollection<LibraryItem> items, CancellationToken cancellationToken = default);
}
