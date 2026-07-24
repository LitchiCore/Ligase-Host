using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed partial class WindowsShortcutResolver
{
    private static readonly HashSet<string> CommandShellNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "wscript.exe",
        "cscript.exe",
        "mshta.exe",
        "rundll32.exe",
        "regsvr32.exe"
    };

    private static readonly HashSet<string> InstallerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec.exe",
        "installutil.exe"
    };

    public IReadOnlyList<WindowsShortcutPreview> ResolveMany(
        IEnumerable<string> shortcutPaths) =>
        shortcutPaths.Select(Resolve).ToArray();

    public WindowsShortcutPreview Resolve(string shortcutPath)
    {
        var displayName = Path.GetFileNameWithoutExtension(shortcutPath?.Trim()) ?? "快捷方式";
        if (string.IsNullOrWhiteSpace(shortcutPath) ||
            !string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            return Failure(shortcutPath ?? string.Empty, displayName, WindowsShortcutErrorCode.NotShortcut);

        string canonicalShortcut;
        try
        {
            canonicalShortcut = Path.GetFullPath(Environment.ExpandEnvironmentVariables(shortcutPath.Trim()));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.NotShortcut);
        }

        if (IsNetworkPath(canonicalShortcut))
            return Failure(canonicalShortcut, displayName, WindowsShortcutErrorCode.NetworkLocation);
        if (!File.Exists(canonicalShortcut))
            return Failure(canonicalShortcut, displayName, WindowsShortcutErrorCode.ShortcutNotFound);

        ShellLinkData data;
        try
        {
            data = ReadShellLink(canonicalShortcut);
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return Failure(canonicalShortcut, displayName, WindowsShortcutErrorCode.DamagedShortcut);
        }

        try
        {
            return Classify(canonicalShortcut, displayName, data);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(canonicalShortcut, displayName, WindowsShortcutErrorCode.DamagedShortcut);
        }
    }

    internal WindowsShortcutPreview Classify(
        string shortcutPath,
        string displayName,
        ShellLinkData data)
    {
        var rawTarget = Environment.ExpandEnvironmentVariables(data.Target.Trim());
        var arguments = data.Arguments.Trim();
        if (rawTarget.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseSteamUri(rawTarget, string.Empty, out var uriAppId))
                return Steam(shortcutPath, displayName, data, null, uriAppId);
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.InvalidSteamShortcut);
        }
        if (arguments.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseSteamUri(string.Empty, arguments, out var argumentUriAppId))
                return Steam(shortcutPath, displayName, data, null, argumentUriAppId);
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.InvalidSteamShortcut);
        }
        if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var uri) && !uri.IsFile)
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.UrlTarget);

        var target = NormalizeTarget(rawTarget, data.WorkingDirectory, shortcutPath);
        if (string.IsNullOrWhiteSpace(target))
        {
            var code = arguments.Contains("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)
                ? WindowsShortcutErrorCode.UwpTarget
                : WindowsShortcutErrorCode.DamagedShortcut;
            return Failure(shortcutPath, displayName, code);
        }
        if (IsNetworkPath(target))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.NetworkLocation);
        if (Directory.Exists(target))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.TargetIsDirectory);
        if (Path.GetFileName(target).Equals("steam.exe", StringComparison.OrdinalIgnoreCase) &&
            TryParseSteamUri(string.Empty, arguments, out var steamAppId))
        {
            if (!File.Exists(target))
                return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.TargetMissing);
            return Steam(shortcutPath, displayName, data, target, steamAppId);
        }

        var extension = Path.GetExtension(target).ToLowerInvariant();
        if (extension is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" or ".wsf")
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.ScriptTarget);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.UnsupportedTarget);
        if (!File.Exists(target))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.TargetMissing);

        var fileName = Path.GetFileName(target);
        if (CommandShellNames.Contains(fileName))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.CommandShellTarget);
        if (InstallerNames.Contains(fileName) || InstallerNamePattern().IsMatch(fileName))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.InstallerTarget);
        if (fileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.Contains("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.UwpTarget);

        var workingDirectory = NormalizeOptionalDirectory(
            data.WorkingDirectory, target, shortcutPath);
        if (workingDirectory is not null && IsNetworkPath(workingDirectory))
            return Failure(shortcutPath, displayName, WindowsShortcutErrorCode.NetworkLocation);
        var iconSource = NormalizeIcon(data.IconLocation, shortcutPath);
        return new WindowsShortcutPreview
        {
            ShortcutPath = shortcutPath,
            DisplayName = displayName,
            Kind = WindowsShortcutPreviewKind.Executable,
            Code = WindowsShortcutErrorCode.None,
            TargetExecutable = target,
            Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            WorkingDirectory = workingDirectory,
            IconSource = iconSource ?? target,
            CanonicalTargetArgumentsKey = CreateCanonicalTargetArgumentsKey(target, arguments)
        };
    }

    public static string CreateCanonicalTargetArgumentsKey(string target, string? arguments)
    {
        var canonicalTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)
            .ToUpperInvariant();
        var canonicalArguments = arguments?.Trim() ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes($"{canonicalTarget}\0{canonicalArguments}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static ShellLinkData ReadShellLink(string shortcutPath)
    {
        var shellLinkType = Type.GetTypeFromCLSID(
            Guid.Parse("00021401-0000-0000-C000-000000000046"),
            throwOnError: true)!;
        var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
        ((IPersistFile)shellLink).Load(shortcutPath, 0);

        try
        {
            var target = new StringBuilder(32768);
            shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var arguments = new StringBuilder(32768);
            shellLink.GetArguments(arguments, arguments.Capacity);
            var workingDirectory = new StringBuilder(32768);
            shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            var icon = new StringBuilder(32768);
            shellLink.GetIconLocation(icon, icon.Capacity, out _);
            return new ShellLinkData(
                target.ToString(),
                arguments.ToString(),
                workingDirectory.ToString(),
                icon.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static WindowsShortcutPreview Steam(
        string shortcutPath,
        string displayName,
        ShellLinkData data,
        string? targetExecutable,
        uint appId) =>
        new()
        {
            ShortcutPath = shortcutPath,
            DisplayName = displayName,
            Kind = WindowsShortcutPreviewKind.SteamShortcut,
            Code = WindowsShortcutErrorCode.None,
            TargetExecutable = targetExecutable,
            SteamAppId = appId,
            IconSource = NormalizeIcon(data.IconLocation, shortcutPath) ?? targetExecutable
        };

    private static WindowsShortcutPreview Failure(
        string shortcutPath,
        string displayName,
        WindowsShortcutErrorCode code) =>
        new()
        {
            ShortcutPath = shortcutPath,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "快捷方式" : displayName,
            Kind = code switch
            {
                WindowsShortcutErrorCode.CommandShellTarget or
                WindowsShortcutErrorCode.InstallerTarget or
                WindowsShortcutErrorCode.ScriptTarget or
                WindowsShortcutErrorCode.NetworkLocation => WindowsShortcutPreviewKind.Risk,
                WindowsShortcutErrorCode.NotShortcut or
                WindowsShortcutErrorCode.ShortcutNotFound or
                WindowsShortcutErrorCode.DamagedShortcut or
                WindowsShortcutErrorCode.TargetMissing => WindowsShortcutPreviewKind.Invalid,
                _ => WindowsShortcutPreviewKind.Unsupported
            },
            Code = code
        };

    private static bool TryParseSteamUri(string target, string arguments, out uint appId)
    {
        appId = 0;
        var candidate = $"{target} {arguments}";
        var match = SteamAppPattern().Match(candidate);
        return match.Success && uint.TryParse(match.Groups["id"].Value, out appId) && appId > 0;
    }

    private static string? NormalizeTarget(
        string value,
        string workingDirectory,
        string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (Path.IsPathFullyQualified(expanded)) return Path.GetFullPath(expanded);

        var baseDirectory = NormalizeOptionalDirectory(
            workingDirectory, null, shortcutPath) ?? Path.GetDirectoryName(shortcutPath)!;
        return Path.GetFullPath(expanded, baseDirectory);
    }

    private static string? NormalizeOptionalDirectory(
        string value,
        string? target,
        string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return target is null ? null : Path.GetDirectoryName(target);
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (Path.IsPathFullyQualified(expanded)) return Path.GetFullPath(expanded);
        return Path.GetFullPath(expanded, Path.GetDirectoryName(shortcutPath)!);
    }

    private static string? NormalizeIcon(string value, string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (IsNetworkPath(expanded)) return null;
        var path = Path.IsPathFullyQualified(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(expanded, Path.GetDirectoryName(shortcutPath)!);
        return File.Exists(path) ? path : null;
    }

    private static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);

    [GeneratedRegex(
        @"(?ix)(?:-applaunch\s+|steam://(?:run|rungameid)/)(?<id>[0-9]+)(?:\s|/|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SteamAppPattern();

    [GeneratedRegex(
        @"(?i)^(?:setup|install|installer|uninstall|uninstaller)(?:[-_. ].*)?\.exe$",
        RegexOptions.CultureInvariant)]
    private static partial Regex InstallerNamePattern();

    internal sealed record ShellLinkData(
        string Target,
        string Arguments,
        string WorkingDirectory,
        string IconLocation);

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumPath,
            IntPtr findData,
            uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int maximumDirectory);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int maximumArguments);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int maximumIconPath,
            out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
