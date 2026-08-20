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

public sealed class LibraryAuthorityService : ILibraryAuthorityService
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ApolloInstanceManager _managedCore;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ApolloCoreEndpoint>>> _discover;
    private readonly Func<ApolloCoreEndpoint, bool, CancellationToken,
        Task<AuthorityReadbackDocument?>>? _readback;

    public LibraryAuthorityService(
        ApolloInstanceManager managedCore,
        ApolloCoreLocator coreLocator)
        : this(
            managedCore,
            coreLocator.DiscoverAsync)
    {
    }

    internal LibraryAuthorityService(
        ApolloInstanceManager managedCore,
        Func<CancellationToken, Task<IReadOnlyList<ApolloCoreEndpoint>>> discover,
        Func<ApolloCoreEndpoint, bool, CancellationToken,
            Task<AuthorityReadbackDocument?>>? readback = null)
    {
        _managedCore = managedCore;
        _discover = discover;
        _readback = readback;
    }

    public async Task<LibraryAuthorityState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var cores = await _discover(cancellationToken);
        if (!_managedCore.IsRunning ||
            !_managedCore.HasStableProcessIdentity ||
            _managedCore.BasePort == 0)
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

        if (cores.Count == 0)
        {
            return State(
                LibraryAuthorityKind.Unavailable,
                "coreStarting",
                "Ligase 串流核心正在启动，游戏库暂时只读；接口就绪后会自动恢复。");
        }

        if (cores.Count > 1)
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "multipleCores",
                "检测到多个 Ligase 核心，无法确定游戏库归属。请关闭其他实例后重新启动 Ligase Host。");
        }

        var core = cores[0];
        if (core.BasePort != _managedCore.BasePort)
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "managedPortMismatch",
                "当前核心与此窗口启动的核心不一致。请重新启动 Ligase Host。");
        }

        var readback = await ReadbackAsync(core, reload: false, cancellationToken);
        if (readback is null ||
            !string.Equals(readback.AuthorityToken, _managedCore.AuthorityToken, StringComparison.Ordinal) ||
            !string.Equals(readback.StartNonce, _managedCore.StartNonce, StringComparison.Ordinal) ||
            !string.Equals(readback.RootFingerprint, _managedCore.RootFingerprint, StringComparison.Ordinal) ||
            (_managedCore.ExpectedUniqueId is not null &&
             !string.Equals(readback.HostUniqueId, _managedCore.ExpectedUniqueId, StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(readback.HostUniqueId, core.UniqueId, StringComparison.OrdinalIgnoreCase))
        {
            return State(
                LibraryAuthorityKind.Ambiguous,
                "authorityMismatch",
                "无法确认当前核心使用的是这个游戏库。请重新启动 Ligase Host。");
        }

        _managedCore.ExpectedUniqueId ??= readback.HostUniqueId;
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
        if (_readback is not null)
            return await _readback(core, reload, cancellationToken);

        try
        {
            using var response = await _client.PostAsJsonAsync(
                core.Endpoint.BuildUri(reload
                    ? "/ligase/v1/authority/reload"
                    : "/ligase/v1/authority/readback"),
                new { token = _managedCore.AuthorityToken },
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
