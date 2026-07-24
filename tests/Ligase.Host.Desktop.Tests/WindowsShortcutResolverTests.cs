using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class WindowsShortcutResolverTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "Ligase.Shortcut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void RealShellLinkResolvesExecutableWithoutExecutingIt()
    {
        var executable = CreateFile("sample.exe");
        var icon = CreateFile("sample.ico");
        var shortcut = Path.Combine(_root, "Sample Game.lnk");
        CreateShortcut(shortcut, executable, "--profile local", _root, icon);

        var preview = new WindowsShortcutResolver().Resolve(shortcut);

        Assert.AreEqual(WindowsShortcutPreviewKind.Executable, preview.Kind);
        Assert.AreEqual(WindowsShortcutErrorCode.None, preview.Code);
        Assert.AreEqual("Sample Game", preview.DisplayName);
        Assert.AreEqual(executable, preview.TargetExecutable);
        Assert.AreEqual("--profile local", preview.Arguments);
        Assert.AreEqual(_root, preview.WorkingDirectory);
        Assert.AreEqual(icon, preview.IconSource);
        Assert.IsNotNull(preview.CanonicalTargetArgumentsKey);
        Assert.IsFalse(preview.ToString().Contains("--profile", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SteamShortcutReturnsTypedSteamAppId()
    {
        var steam = CreateFile("steam.exe");
        var shortcut = Path.Combine(_root, "Steam Game.lnk");
        CreateShortcut(shortcut, steam, "-applaunch 3548580", _root, null);

        var preview = new WindowsShortcutResolver().Resolve(shortcut);

        Assert.AreEqual(WindowsShortcutPreviewKind.SteamShortcut, preview.Kind);
        Assert.AreEqual((uint)3548580, preview.SteamAppId);
        Assert.IsFalse(preview.CanConfirmExecutable);
        Assert.IsNull(preview.CanonicalTargetArgumentsKey);
    }

    [TestMethod]
    public void SteamUriArgumentReturnsTypedSteamAppIdWithoutExecutableFallback()
    {
        var preview = new WindowsShortcutResolver().Classify(
            Path.Combine(_root, "Steam URI.lnk"),
            "Steam URI",
            new WindowsShortcutResolver.ShellLinkData(
                string.Empty,
                "steam://rungameid/3548580",
                string.Empty,
                string.Empty));

        Assert.AreEqual(WindowsShortcutPreviewKind.SteamShortcut, preview.Kind);
        Assert.AreEqual((uint)3548580, preview.SteamAppId);
        Assert.IsNull(preview.TargetExecutable);
        Assert.IsFalse(preview.CanConfirmExecutable);
    }

    [TestMethod]
    public void RiskAndUnsupportedTargetsFailClosed()
    {
        var resolver = new WindowsShortcutResolver();
        var cmd = CreateFile("cmd.exe");
        var setup = CreateFile("setup-game.exe");
        var script = CreateFile("launch.cmd");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "folder")).FullName;

        AssertClassification(resolver, cmd, WindowsShortcutErrorCode.CommandShellTarget);
        AssertClassification(resolver, setup, WindowsShortcutErrorCode.InstallerTarget);
        AssertClassification(resolver, script, WindowsShortcutErrorCode.ScriptTarget);
        AssertClassification(resolver, directory, WindowsShortcutErrorCode.TargetIsDirectory);
        AssertClassification(
            resolver,
            @"\\server\share\game.exe",
            WindowsShortcutErrorCode.NetworkLocation);
    }

    [TestMethod]
    public void InvalidInputsAndUwpProjectionDoNotBecomeExecutablePreviews()
    {
        var resolver = new WindowsShortcutResolver();
        var nonShortcut = resolver.Resolve(Path.Combine(_root, "game.exe"));
        var missing = resolver.Resolve(Path.Combine(_root, "missing.lnk"));
        var uwp = resolver.Classify(
            Path.Combine(_root, "App.lnk"),
            "App",
            new WindowsShortcutResolver.ShellLinkData(
                string.Empty,
                "shell:AppsFolder\\Microsoft.Sample_123!App",
                string.Empty,
                string.Empty));

        Assert.AreEqual(WindowsShortcutErrorCode.NotShortcut, nonShortcut.Code);
        Assert.AreEqual(WindowsShortcutErrorCode.ShortcutNotFound, missing.Code);
        Assert.AreEqual(WindowsShortcutErrorCode.UwpTarget, uwp.Code);
        Assert.IsFalse(uwp.CanConfirmExecutable);
    }

    [TestMethod]
    public void ResolveManyPreservesInputOrderAndClassifiesEachShortcutIndependently()
    {
        var executable = CreateFile("first.exe");
        var validShortcut = Path.Combine(_root, "First.lnk");
        var missingShortcut = Path.Combine(_root, "Missing.lnk");
        CreateShortcut(validShortcut, executable, string.Empty, _root, null);

        var previews = new WindowsShortcutResolver().ResolveMany(
            [validShortcut, missingShortcut]);

        Assert.AreEqual(2, previews.Count);
        Assert.AreEqual("First", previews[0].DisplayName);
        Assert.AreEqual(WindowsShortcutPreviewKind.Executable, previews[0].Kind);
        Assert.AreEqual(executable, previews[0].IconSource);
        Assert.AreEqual("Missing", previews[1].DisplayName);
        Assert.AreEqual(WindowsShortcutErrorCode.ShortcutNotFound, previews[1].Code);
    }

    [TestMethod]
    public void RelativeExecutableAndWorkingDirectoryAreNormalizedFromShortcutLocation()
    {
        var executable = CreateFile("relative.exe");
        var preview = new WindowsShortcutResolver().Classify(
            Path.Combine(_root, "Relative.lnk"),
            "Relative",
            new WindowsShortcutResolver.ShellLinkData(
                "relative.exe",
                string.Empty,
                ".",
                string.Empty));

        Assert.AreEqual(WindowsShortcutPreviewKind.Executable, preview.Kind);
        Assert.AreEqual(executable, preview.TargetExecutable);
        Assert.AreEqual(_root, preview.WorkingDirectory);
    }

    [TestMethod]
    public void CanonicalDuplicateKeyUsesTargetAndArgumentsButDoesNotExposeThem()
    {
        var executable = CreateFile("game.exe");
        var same = WindowsShortcutResolver.CreateCanonicalTargetArgumentsKey(
            executable.ToUpperInvariant(), " --slot 1 ");
        var original = WindowsShortcutResolver.CreateCanonicalTargetArgumentsKey(
            executable, "--slot 1");
        var otherArguments = WindowsShortcutResolver.CreateCanonicalTargetArgumentsKey(
            executable, "--slot 2");

        Assert.AreEqual(original, same);
        Assert.AreNotEqual(original, otherArguments);
        Assert.AreEqual(64, original.Length);
        Assert.IsFalse(original.Contains("slot", StringComparison.OrdinalIgnoreCase));
    }

    private void AssertClassification(
        WindowsShortcutResolver resolver,
        string target,
        WindowsShortcutErrorCode code)
    {
        var preview = resolver.Classify(
            Path.Combine(_root, $"{Guid.NewGuid():N}.lnk"),
            "Candidate",
            new WindowsShortcutResolver.ShellLinkData(
                target, string.Empty, _root, string.Empty));
        Assert.AreEqual(code, preview.Code);
        Assert.IsFalse(preview.CanConfirmExecutable);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static void CreateShortcut(
        string shortcut,
        string target,
        string arguments,
        string workingDirectory,
        string? icon)
    {
        var shellLinkType = Type.GetTypeFromCLSID(
            Guid.Parse("00021401-0000-0000-C000-000000000046"),
            throwOnError: true)!;
        var shellLink = (ITestShellLink)Activator.CreateInstance(shellLinkType)!;
        shellLink.SetPath(target);
        shellLink.SetArguments(arguments);
        shellLink.SetWorkingDirectory(workingDirectory);
        if (icon is not null) shellLink.SetIconLocation(icon, 0);
        ((IPersistFile)shellLink).Save(shortcut, true);
        Marshal.FinalReleaseComObject(shellLink);
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITestShellLink
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
