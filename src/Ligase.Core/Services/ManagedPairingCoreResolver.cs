using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface IManagedPairingCoreResolver
{
    Task<ManagedPairingCore> ResolveAsync(
        CancellationToken cancellationToken = default);
}
public sealed class ManagedPairingCoreResolver(
    ApolloInstanceManager managedCore,
    ApolloCoreLocator locator) : IManagedPairingCoreResolver
{
    public async Task<ManagedPairingCore> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!managedCore.IsRunning ||
            !managedCore.HasStableProcessIdentity ||
            managedCore.BasePort == 0 ||
            string.IsNullOrWhiteSpace(managedCore.StartNonce) ||
            string.IsNullOrWhiteSpace(managedCore.ExpectedUniqueId))
        {
            throw new AttendedPairingUnavailableException(
                "当前窗口没有唯一受管理的 Ligase 核心。请重新启动 Ligase Host 后重试。");
        }

        var endpoint = await locator.ResolveAsync(cancellationToken);
        if (endpoint.BasePort != managedCore.BasePort ||
            !string.Equals(
                endpoint.UniqueId,
                managedCore.ExpectedUniqueId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AttendedPairingUnavailableException(
                "检测到核心实例身份不一致。请关闭其他 Ligase Host 实例并重新启动。");
        }

        return new ManagedPairingCore(
            endpoint.Endpoint,
            endpoint.UniqueId,
            managedCore.StartNonce,
            endpoint.BasePort);
    }
}

public sealed class AttendedPairingUnavailableException(string message)
    : InvalidOperationException(message);
