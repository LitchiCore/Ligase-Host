using System.Net.Http.Json;

namespace Ligase.Host.Core.Services;

public sealed record VirtualDisplayState(
    int SchemaVersion,
    string State,
    string Reason,
    string DisplayName,
    bool DriverReady,
    bool WindowsDisplayPresent,
    bool StreamingAvailable,
    bool UsedByActiveStream,
    string StreamSource)
{
    public bool IsEnabled =>
        State == "enabled" && DriverReady && WindowsDisplayPresent;

    public string StatusText => State switch
    {
        "enabled" when WindowsDisplayPresent =>
            UsedByActiveStream
                ? $"串流正在使用 · {DisplayName}"
                : $"已启用 · {DisplayName} · 虚拟桌面串流会复用此显示器",
        "disabled" when DriverReady => "驱动已就绪 · 虚拟桌面当前未启用",
        "unavailable" => Reason switch
        {
            "streamActive" => "串流进行中，暂时不能改变虚拟桌面状态",
            "driverUnavailable" => "SudoVDA 驱动接口不可用",
            "createFailed" => "虚拟显示创建失败",
            "removeFailed" => "虚拟显示停用失败",
            "readbackFailed" => "Windows 显示状态回读失败",
            _ => "虚拟桌面暂不可用"
        },
        _ => "虚拟桌面状态未知"
    };
}

public interface IVirtualDisplayControlService
{
    Task<VirtualDisplayState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<VirtualDisplayState> EnableAsync(CancellationToken cancellationToken = default);
    Task<VirtualDisplayState> DisableAsync(CancellationToken cancellationToken = default);
}

public sealed class VirtualDisplayControlService(ApolloCoreLocator coreLocator) :
    IVirtualDisplayControlService, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim _operation = new(1, 1);

    public Task<VirtualDisplayState> GetStateAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync(HttpMethod.Get, "/ligase/v1/virtual-display", cancellationToken);

    public Task<VirtualDisplayState> EnableAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync(HttpMethod.Post, "/ligase/v1/virtual-display/enable", cancellationToken);

    public Task<VirtualDisplayState> DisableAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync(HttpMethod.Post, "/ligase/v1/virtual-display/disable", cancellationToken);

    private async Task<VirtualDisplayState> InvokeAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!await _operation.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("已有虚拟桌面操作正在进行，请稍候。");
        try
        {
            var core = await coreLocator.ResolveAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, core.Endpoint.BuildUri(path));
            using var response = await _client.SendAsync(request, cancellationToken);
            var state = await response.Content.ReadFromJsonAsync<VirtualDisplayState>(
                cancellationToken: cancellationToken);
            if (state is null || state.SchemaVersion != 1 ||
                state.State is not ("enabled" or "disabled" or "unavailable") ||
                state.StreamSource != "apolloVirtualDisplayApp")
                throw new InvalidOperationException("核心返回了无效的虚拟桌面状态。");
            if (!response.IsSuccessStatusCode && state.State != "unavailable")
                throw new InvalidOperationException("虚拟桌面操作失败，核心没有返回可用状态。");
            return state;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("虚拟桌面操作超时，Windows 显示状态未能确认。");
        }
        finally
        {
            _operation.Release();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _operation.Dispose();
    }
}
