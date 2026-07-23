using System.Net.Http.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface ILibraryAuthorityService
{
    Task<LibraryAuthorityState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<AuthorityReadbackDocument> RequireReadbackAsync(
        ApolloCoreEndpoint core,
        bool reload,
        CancellationToken cancellationToken = default);
}

public sealed class LibraryAuthorityService(
    ApolloInstanceManager managedCore,
    ApolloCoreLocator coreLocator) : ILibraryAuthorityService
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<LibraryAuthorityState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var cores = await coreLocator.DiscoverAsync(cancellationToken);
        if (!managedCore.IsRunning ||
            !managedCore.HasStableProcessIdentity ||
            managedCore.BasePort == 0)
        {
            return cores.Count switch
            {
                0 => State(
                    LibraryAuthorityKind.Unavailable,
                    "coreUnavailable",
                    "Ligase 串流核心没有运行。请重新启动 Ligase Host。"),
                1 => State(
                    LibraryAuthorityKind.ExternalUnknown,
                    "externalCore",
                    "当前检测到的核心不由此窗口管理。为防止游戏写入错误位置，游戏库暂时只读。请关闭其他实例后重新启动 Ligase Host。"),
                _ => State(
                    LibraryAuthorityKind.Ambiguous,
                    "multipleCores",
                    "检测到多个 Ligase 核心。请关闭其他实例后重新启动 Ligase Host。")
            };
        }

        if (cores.Count != 1)
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "multipleCores",
                "检测到多个 Ligase 核心，无法确定游戏库归属。请关闭其他实例后重新启动 Ligase Host。");
        }

        var core = cores[0];
        if (core.BasePort != managedCore.BasePort)
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "managedPortMismatch",
                "当前核心与此窗口启动的核心不一致。请重新启动 Ligase Host。");
        }

        var readback = await ReadbackAsync(core, reload: false, cancellationToken);
        if (readback is null ||
            !string.Equals(readback.AuthorityToken, managedCore.AuthorityToken, StringComparison.Ordinal) ||
            !string.Equals(readback.StartNonce, managedCore.StartNonce, StringComparison.Ordinal) ||
            !string.Equals(readback.RootFingerprint, managedCore.RootFingerprint, StringComparison.Ordinal) ||
            (managedCore.ExpectedUniqueId is not null &&
             !string.Equals(readback.HostUniqueId, managedCore.ExpectedUniqueId, StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(readback.HostUniqueId, core.UniqueId, StringComparison.OrdinalIgnoreCase))
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "authorityMismatch",
                "无法确认当前核心使用的是这个游戏库。请重新启动 Ligase Host。");
        }

        managedCore.ExpectedUniqueId ??= readback.HostUniqueId;
        return new LibraryAuthorityState(
            LibraryAuthorityKind.ManagedAuthoritative,
            "managedAuthoritative",
            "游戏库已连接到当前 Ligase 核心。",
            core);
    }

    public async Task<AuthorityReadbackDocument> RequireReadbackAsync(
        ApolloCoreEndpoint core,
        bool reload,
        CancellationToken cancellationToken = default) =>
        await ReadbackAsync(core, reload, cancellationToken)
        ?? throw new InvalidOperationException(
            "核心没有确认游戏库更新。已恢复更新前状态，请重新启动 Ligase Host 后再试。");

    private async Task<AuthorityReadbackDocument?> ReadbackAsync(
        ApolloCoreEndpoint core,
        bool reload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(
                core.Endpoint.BuildUri(reload
                    ? "/ligase/v1/authority/reload"
                    : "/ligase/v1/authority/readback"),
                new { token = managedCore.AuthorityToken },
                cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<AuthorityReadbackDocument>(
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static LibraryAuthorityState State(
        LibraryAuthorityKind kind,
        string code,
        string message) => new(kind, code, message);
}
