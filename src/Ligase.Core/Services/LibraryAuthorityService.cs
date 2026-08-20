using System.Net.Http.Json;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
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
    internal static readonly TimeSpan ReadbackDeadline = TimeSpan.FromSeconds(3);
    private readonly HttpClient _client;
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
            Task<AuthorityReadbackDocument?>>? readback = null,
        HttpClient? client = null)
    {
        _managedCore = managedCore;
        _discover = discover;
        _readback = readback;
        _client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
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

        AuthorityReadbackDocument? readback;
        try
        {
            readback = await ReadbackAsync(core, reload: false, cancellationToken);
        }
        catch (LibraryAuthorityReadbackException)
        {
            readback = null;
        }
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
        await ReadbackAsync(core, reload, cancellationToken);

    private async Task<AuthorityReadbackDocument> ReadbackAsync(
        ApolloCoreEndpoint core,
        bool reload,
        CancellationToken cancellationToken)
    {
        if (_readback is not null)
            return await _readback(core, reload, cancellationToken)
                ?? throw Failure(reload, "unavailable", "response", "emptyResponse");

        var stopwatch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadbackDeadline);
        try
        {
            using var response = await _client.PostAsJsonAsync(
                core.Endpoint.BuildUri(reload
                    ? "/ligase/v1/authority/reload"
                    : "/ligase/v1/authority/readback"),
                new { token = _managedCore.AuthorityToken },
                deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                if (reload && !IsExpectedReloadStatus(response.StatusCode))
                    throw Failure(
                        true,
                        "invalidResponse",
                        "response",
                        "unexpectedHttpStatus",
                        response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                var typed = await TryReadFailureAsync(response, deadline.Token);
                if (reload && (typed is null ||
                               !IsExpectedReloadFailure(response.StatusCode, typed)))
                    throw Failure(
                        true,
                        "invalidResponse",
                        "semantic",
                        "reloadMetadataMismatch",
                        response.StatusCode,
                        typed?.ElapsedMs ?? stopwatch.ElapsedMilliseconds);
                throw Failure(
                    reload,
                    typed?.ResultCode ?? "httpRejected",
                    typed?.Stage ?? "response",
                    typed?.ReasonCode ?? $"http{(int)response.StatusCode}",
                    response.StatusCode,
                    typed?.ElapsedMs ?? stopwatch.ElapsedMilliseconds);
            }

            AuthorityReadbackDocument document;
            try
            {
                document = await response.Content.ReadFromJsonAsync<AuthorityReadbackDocument>(
                    cancellationToken: deadline.Token)
                    ?? throw new JsonException("empty authority readback");
            }
            catch (JsonException exception)
            {
                throw Failure(reload, "invalidResponse", "deserialize", "invalidJson",
                    response.StatusCode, stopwatch.ElapsedMilliseconds, exception);
            }

            if (reload && (document.Reload is null ||
                           document.Reload.SchemaVersion != 1 ||
                           !string.Equals(document.Reload.ResultCode, "completed", StringComparison.Ordinal) ||
                           !string.Equals(document.Reload.Stage, "readback", StringComparison.Ordinal) ||
                           !string.Equals(document.Reload.ReasonCode, "none", StringComparison.Ordinal) ||
                           document.Reload.ElapsedMs < 0))
                throw Failure(true, "invalidResponse", "semantic", "reloadMetadataMismatch",
                    response.StatusCode, stopwatch.ElapsedMilliseconds);
            return document;
        }
        catch (LibraryAuthorityReadbackException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(reload, "timeout", "transport", "deadlineExceeded", null,
                stopwatch.ElapsedMilliseconds, exception);
        }
        catch (HttpRequestException exception)
        {
            var status = NormalizeStatus(exception.StatusCode);
            throw Failure(reload, "transportFailed", "transport",
                status is null ? "httpRequestFailed" : "httpStatusFailure",
                status, stopwatch.ElapsedMilliseconds, exception);
        }
    }

    private static async Task<AuthorityReloadResult?> TryReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("reload", out var reload)) return null;
            return reload.Deserialize<AuthorityReloadResult>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsExpectedReloadFailure(
        HttpStatusCode status,
        AuthorityReloadResult reload)
    {
        if (reload.SchemaVersion != 1 || reload.ElapsedMs < 0) return false;
        return (status, reload.ResultCode, reload.Stage, reload.ReasonCode) switch
        {
            (HttpStatusCode.Forbidden, "rejected", "requestValidation", "loopbackOnly") => true,
            (HttpStatusCode.Forbidden, "rejected", "authorityValidation", "authorityMismatch") => true,
            (HttpStatusCode.NotFound, "unavailable", "authorityValidation", "authorityUnavailable") => true,
            (HttpStatusCode.Conflict, "rejected", "precondition", "sessionActive") => true,
            (HttpStatusCode.InternalServerError, "failed", "appCatalogReload", "reloadFailed") => true,
            (HttpStatusCode.InternalServerError, "failed", "appCatalogParse", "catalogMalformed") => true,
            (HttpStatusCode.InternalServerError, "failed", "appCatalogLoad", "catalogUnreadable") => true,
            (HttpStatusCode.InternalServerError, "failed", "appCatalogLoad", "catalogLoadFailed") => true,
            (HttpStatusCode.InternalServerError, "failed", "appCatalogSwap", "catalogSwapFailed") => true,
            _ => false
        };
    }

    private static bool IsExpectedReloadStatus(HttpStatusCode status) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or
            HttpStatusCode.Conflict or HttpStatusCode.InternalServerError;

    private static HttpStatusCode? NormalizeStatus(HttpStatusCode? status) =>
        status is not null && (int)status >= 100 && (int)status <= 599 ? status : null;

    private static LibraryAuthorityReadbackException Failure(
        bool reload,
        string resultCode,
        string stage,
        string reasonCode,
        HttpStatusCode? httpStatus = null,
        long? elapsedMs = null,
        Exception? inner = null)
    {
        var failure = new LibraryAuthorityReadbackFailure(
                resultCode,
                reload ? $"reload.{stage}" : $"readback.{stage}",
                reasonCode,
                httpStatus is null ? null : (int)httpStatus,
                elapsedMs);
        if (reload)
            LibraryMutationOutcomeSemanticValidator.ValidateAttempt(
                new LibraryMutationAttempt(
                    failure.ResultCode, failure.Stage, failure.ReasonCode,
                    failure.HttpStatusCode, failure.ElapsedMs),
                "addSteam");
        return new LibraryAuthorityReadbackException(
            failure, MessageFor(resultCode, reasonCode), inner);
    }

    private static string MessageFor(string resultCode, string reasonCode) =>
        (resultCode, reasonCode) switch
        {
            ("rejected", "sessionActive") =>
                "当前仍有活动串流或游戏会话，核心拒绝刷新游戏库；已恢复更新前状态。请结束会话后再试。",
            ("failed", "reloadFailed") =>
                "核心无法加载新的游戏目录；已恢复更新前状态。请查看游戏库诊断后再试。",
            ("failed", "catalogMalformed") =>
                "游戏目录格式无效，核心保留了原游戏库；已恢复更新前状态。请查看游戏库诊断。",
            ("failed", "catalogUnreadable") =>
                "核心无法读取新的游戏目录，已保留原游戏库；已恢复更新前状态。",
            ("failed", "catalogLoadFailed") or ("failed", "catalogSwapFailed") =>
                "核心未能安全载入新的游戏目录，已保留原游戏库；已恢复更新前状态。请查看游戏库诊断。",
            ("rejected", "loopbackOnly") =>
                "游戏库刷新请求未从本机受信通道发出；已恢复更新前状态。",
            ("rejected", "authorityMismatch") or ("unavailable", "authorityUnavailable") =>
                "核心身份校验失败，未接受游戏库刷新；已恢复更新前状态。请重新启动 Ligase Host 后再试。",
            ("timeout", _) =>
                "核心未在游戏库刷新期限内确认更新；已恢复更新前状态。请查看游戏库诊断后再试。",
            ("transportFailed", _) =>
                "无法连接当前核心以确认游戏库更新；已恢复更新前状态。请确认核心仍在运行。",
            ("invalidResponse", _) =>
                "核心返回了无法验证的游戏库刷新结果；已恢复更新前状态。请查看游戏库诊断。",
            _ =>
                "核心没有确认游戏库更新；已恢复更新前状态。请查看游戏库诊断后再试。"
        };

    private static LibraryAuthorityState State(
        LibraryAuthorityKind kind,
        string code,
        string message) => new(kind, code, message);
}

public sealed record LibraryAuthorityReadbackFailure(
    string ResultCode,
    string Stage,
    string ReasonCode,
    int? HttpStatusCode,
    long? ElapsedMs);

public sealed class LibraryAuthorityReadbackException : InvalidOperationException
{
    public LibraryAuthorityReadbackException(
        LibraryAuthorityReadbackFailure failure,
        string message,
        Exception? innerException = null) : base(message, innerException) =>
        Failure = failure;

    public LibraryAuthorityReadbackFailure Failure { get; }
}
