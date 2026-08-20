using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.ViewModels;
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
        var linkedRoot = Path.Combine(_root, "linked-root");
        if (Directory.Exists(linkedRoot) &&
            (File.GetAttributes(linkedRoot) & FileAttributes.ReparsePoint) != 0)
            Directory.Delete(linkedRoot);
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
    public void DropPreviewIsReadOnlyAndBulkOrDuplicateInputFailsClosed()
    {
        var executable = CreateFile("preview.exe");
        var shortcut = Path.Combine(_root, "Preview.lnk");
        CreateShortcut(shortcut, executable, "--preview", _root, null);
        var viewModel = new AddApplicationViewModel(
            null!, null!, null!, null!, null!, new WindowsShortcutResolver());

        viewModel.PreviewShortcutPaths([shortcut]);
        Assert.IsTrue(viewModel.HasShortcutPreview);
        Assert.AreEqual(executable, viewModel.ShortcutPreview?.TargetExecutable);

        viewModel.PreviewShortcutPaths([shortcut, shortcut]);
        Assert.IsFalse(viewModel.HasShortcutPreview);
        StringAssert.Contains(viewModel.Message, "一次只能预览一个");

        viewModel.PreviewShortcutPaths([]);
        Assert.IsFalse(viewModel.HasShortcutPreview);
        viewModel.CancelShortcutPreview();
        Assert.IsFalse(viewModel.HasShortcutPreview);
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

    [TestMethod]
    public void ConfirmationRejectsShortcutAndTargetDriftAfterPreview()
    {
        var resolver = new WindowsShortcutResolver();
        var executable = CreateFile("drift.exe");
        var shortcut = Path.Combine(_root, "Drift.lnk");
        CreateShortcut(shortcut, executable, "--safe", _root, null);
        var preview = resolver.Resolve(shortcut);

        Assert.IsTrue(preview.CanConfirmExecutable);
        Assert.AreEqual(64, preview.AuthoritySha256?.Length);

        File.WriteAllBytes(executable, [1, 2, 3]);
        var targetDrift = resolver.Revalidate(preview);
        Assert.AreEqual(WindowsShortcutErrorCode.AuthorityChanged, targetDrift.Code);
        Assert.IsFalse(targetDrift.CanConfirmExecutable);

        File.WriteAllBytes(executable, [0]);
        CreateShortcut(shortcut, executable, "--changed", _root, null);
        var shortcutDrift = resolver.Revalidate(preview);
        Assert.AreEqual(WindowsShortcutErrorCode.AuthorityChanged, shortcutDrift.Code);
        Assert.IsFalse(shortcutDrift.CanConfirmExecutable);
    }

    [TestMethod]
    public void ReparsePathFailsClosedWithoutExecutingTarget()
    {
        var real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        var executable = Path.Combine(real, "linked.exe");
        File.WriteAllBytes(executable, [0]);
        var link = Path.Combine(_root, "linked-root");
        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(real);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
        }

        var shortcut = Path.Combine(_root, "Linked.lnk");
        CreateShortcut(shortcut, Path.Combine(link, "linked.exe"), string.Empty, link, null);
        var preview = new WindowsShortcutResolver().Resolve(shortcut);

        Assert.AreEqual(WindowsShortcutErrorCode.ReparsePoint, preview.Code);
        Assert.IsFalse(preview.CanConfirmExecutable);
    }

    [TestMethod]
    public void LocalMatchUsesOnlyVerifiedSteamInstallContainment()
    {
        var library = Directory.CreateDirectory(Path.Combine(_root, "steamlib")).FullName;
        var install = Directory.CreateDirectory(Path.Combine(
            library, "steamapps", "common", "Game")).FullName;
        var target = Path.Combine(install, "bin", "game.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, [0]);
        var preview = ExecutablePreview(target);
        var game = new SteamGame(
            3548580, "Chill", "Game", library,
            Path.Combine(_root, "appmanifest_3548580.acf"), 1);

        var match = new WindowsShortcutLocalMatchService().Inspect(preview, [game]);

        Assert.AreEqual(WindowsShortcutMatchKind.ExactSteamInstall, match.Kind);
        Assert.AreEqual((uint)3548580, match.SteamAppId);
        Assert.IsFalse(match.RequiresExplicitFallbackConfirmation);
    }

    [TestMethod]
    public void MultipleInstallAuthoritiesRequireExplicitLocalFallback()
    {
        var library = Directory.CreateDirectory(Path.Combine(_root, "steamlib")).FullName;
        var parent = Directory.CreateDirectory(Path.Combine(
            library, "steamapps", "common", "Parent")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(parent, "Nested")).FullName;
        var target = Path.Combine(nested, "game.exe");
        File.WriteAllBytes(target, [0]);
        var games = new[]
        {
            new SteamGame(1, "Parent", "Parent", library, Path.Combine(_root, "one.acf"), 1),
            new SteamGame(2, "Nested", "Parent\\Nested", library, Path.Combine(_root, "two.acf"), 1)
        };

        var match = new WindowsShortcutLocalMatchService().Inspect(
            ExecutablePreview(target), games);

        Assert.AreEqual(WindowsShortcutMatchKind.MultipleSteamInstalls, match.Kind);
        Assert.IsNull(match.SteamAppId);
        Assert.IsTrue(match.RequiresExplicitFallbackConfirmation);
    }

    [TestMethod]
    public void SimilarNameOrPathPrefixNeverCreatesSteamAuthority()
    {
        var library = Directory.CreateDirectory(Path.Combine(_root, "steamlib")).FullName;
        var common = Directory.CreateDirectory(Path.Combine(library, "steamapps", "common")).FullName;
        var install = Directory.CreateDirectory(Path.Combine(common, "Game")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(common, "Game-Other")).FullName;
        var target = Path.Combine(sibling, "game.exe");
        File.WriteAllBytes(target, [0]);
        var game = new SteamGame(
            42, "game", "Game", library, Path.Combine(_root, "42.acf"), 1);

        var match = new WindowsShortcutLocalMatchService().Inspect(
            ExecutablePreview(target), [game]);

        Assert.AreEqual(WindowsShortcutMatchKind.LocalExecutable, match.Kind);
        Assert.IsNull(match.SteamAppId);
    }

    [TestMethod]
    public void PublicShortcutRouteRequiresAsyncLocalAuthorityAndExplicitConfirmation()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "AddApplicationPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "ViewModels", "AddApplicationViewModel.cs"));

        Assert.AreEqual(3, Count(page, "PreviewShortcutPathsAsync("));
        Assert.IsFalse(page.Contains("PreviewShortcutPaths(", StringComparison.Ordinal));
        StringAssert.Contains(viewModel, "shortcutResolver.Revalidate(pending)");
        StringAssert.Contains(viewModel, "_shortcutMatchService.Inspect(current, games)");
        StringAssert.Contains(viewModel, "currentMatch != ShortcutMatch");
        StringAssert.Contains(viewModel, "RequiresExplicitFallbackConfirmation");
        StringAssert.Contains(viewModel, "mutationCoordinator.AddSteamAsync");
        StringAssert.Contains(viewModel, "mutationCoordinator.AddExecutableAsync");
        Assert.IsFalse(viewModel.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(viewModel.Contains("ShellExecute", StringComparison.Ordinal));
    }

    private static WindowsShortcutPreview ExecutablePreview(string target) => new()
    {
        ShortcutPath = target + ".lnk",
        DisplayName = Path.GetFileNameWithoutExtension(target),
        Kind = WindowsShortcutPreviewKind.Executable,
        Code = WindowsShortcutErrorCode.None,
        TargetExecutable = target
    };

    private static int Count(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
    }

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if ((Directory.Exists(gitMarker) || File.Exists(gitMarker)) &&
                File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
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
