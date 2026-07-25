using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Ligase.Host.Launcher;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var launcher = Environment.ProcessPath
                ?? throw new InvalidOperationException("launcherPathUnavailable");
            var target = LauncherManifestValidator.Resolve(launcher);
            var start = CreateStartInfo(target.ExecutablePath, args);
            using var child = Process.Start(start)
                ?? throw new InvalidOperationException("launcherDesktopStartFailed");
            child.WaitForExit();
            return child.ExitCode;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException or
                InvalidOperationException)
        {
            ShowError(MapMachineCode(exception));
            return 70;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    private static string MapMachineCode(Exception exception) =>
        exception is InvalidDataException &&
        exception.Message is
            "launcherDesktopUnavailable" or
            "launcherDesktopHashMismatch"
            ? exception.Message
            : exception is UnauthorizedAccessException
                ? "launcherAccessDenied"
                : "launcherManifestInvalid";

    private static void ShowError(string code) =>
        _ = MessageBox(
            IntPtr.Zero,
            $"Ligase Host could not start ({code}). Repair the installation and try again.",
            "Ligase Host",
            0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr window,
        string text,
        string caption,
        uint type);
}
