using System.Diagnostics;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class WindowsShortcutLocalMatchService
{
    public WindowsShortcutLocalMatch Inspect(
        WindowsShortcutPreview preview,
        IReadOnlyCollection<SteamGame> installedSteamGames)
    {
        if (!preview.CanConfirmExecutable ||
            string.IsNullOrWhiteSpace(preview.TargetExecutable) ||
            !File.Exists(preview.TargetExecutable))
            throw new InvalidOperationException("shortcutTargetUnavailable");

        var target = Path.GetFullPath(preview.TargetExecutable);
        var version = FileVersionInfo.GetVersionInfo(target);
        var matches = installedSteamGames
            .Where(game => IsContained(target, game.InstallPath))
            .GroupBy(game => game.AppId)
            .Select(group => group.First())
            .OrderBy(game => game.AppId)
            .ToArray();

        var metadata = (
            ProductName: Clean(version.ProductName),
            Description: Clean(version.FileDescription),
            Version: Clean(version.FileVersion),
            Company: Clean(version.CompanyName));
        if (matches.Length == 1)
        {
            var game = matches[0];
            return new WindowsShortcutLocalMatch(
                WindowsShortcutMatchKind.ExactSteamInstall,
                metadata.ProductName,
                metadata.Description,
                metadata.Version,
                metadata.Company,
                game.AppId,
                $"已按本机 Steam manifest 与安装目录唯一匹配 App ID {game.AppId}；确认后使用 Steam 权威链添加。");
        }
        if (matches.Length > 1)
            return new WindowsShortcutLocalMatch(
                WindowsShortcutMatchKind.MultipleSteamInstalls,
                metadata.ProductName,
                metadata.Description,
                metadata.Version,
                metadata.Company,
                null,
                "目标同时落入多个本机 Steam 安装目录；不会猜测 App ID。若按普通本地应用添加，需额外确认。");

        return new WindowsShortcutLocalMatch(
            WindowsShortcutMatchKind.LocalExecutable,
            metadata.ProductName,
            metadata.Description,
            metadata.Version,
            metadata.Company,
            null,
            "未发现可由本机 Steam manifest 唯一证明的匹配；将按普通本地应用处理。");
    }

    private static bool IsContained(string target, string installPath)
    {
        try
        {
            var root = Path.GetFullPath(installPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未提供" : value.Trim();
}
