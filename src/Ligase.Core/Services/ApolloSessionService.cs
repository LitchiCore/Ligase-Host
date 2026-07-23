using System.Net.Http.Json;

namespace Ligase.Host.Core.Services;

public sealed record ApolloCancelResult(bool Cancelled, string SessionState);

public sealed class ApolloSessionService(ApolloCoreLocator coreLocator)
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<ApolloCancelResult> CancelAsync(
        CancellationToken cancellationToken = default)
    {
        var core = await coreLocator.ResolveAsync(cancellationToken);
        using var response = await _client.PostAsync(
            core.Endpoint.BuildUri("/ligase/v1/session/cancel"),
            content: null,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "当前 Ligase 核心还不支持从 Host 结束串流。请升级核心后重试。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "未能结束串流。请确认核心仍在运行，然后重试。");
        return await response.Content.ReadFromJsonAsync<ApolloCancelResult>(
                   cancellationToken: cancellationToken)
               ?? new ApolloCancelResult(false, "free");
    }
}
