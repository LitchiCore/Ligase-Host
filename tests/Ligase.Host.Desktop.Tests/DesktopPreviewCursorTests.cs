using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class DesktopPreviewCursorTests
{
    [TestMethod]
    public void CursorPlanUsesHotspotAndMonitorRelativeScaling()
    {
        var plan = CursorOverlayPlanner.Plan(
            cursorX: 2020,
            cursorY: 180,
            hotspotX: 10,
            hotspotY: 5,
            cursorWidth: 32,
            cursorHeight: 32,
            source: new PreviewRectangle(1920, 0, 3840, 1080),
            destinationWidth: 800,
            destinationHeight: 450);

        Assert.IsTrue(plan.Visible);
        Assert.AreEqual(38, plan.X);
        Assert.AreEqual(73, plan.Y);
        Assert.AreEqual(13, plan.Width);
        Assert.AreEqual(13, plan.Height);
    }

    [TestMethod]
    public void CursorOutsideSelectedPhysicalOrVirtualMonitorIsNotComposited()
    {
        var physical = CursorOverlayPlanner.Plan(
            2500, 400, 0, 0, 32, 32,
            new PreviewRectangle(0, 0, 1920, 1080), 800, 450);
        var virtualDisplay = CursorOverlayPlanner.Plan(
            500, 400, 0, 0, 32, 32,
            new PreviewRectangle(1920, 0, 3840, 1080), 800, 450);

        Assert.IsFalse(physical.Visible);
        Assert.IsFalse(virtualDisplay.Visible);
    }

    [TestMethod]
    public void PreviewGateRejectsFramesAfterStopOrDisplaySwitch()
    {
        var gate = new PreviewFrameGate();
        gate.Start(@"\\.\DISPLAY1");
        var physical = gate.Capture();
        Assert.IsTrue(gate.CanPublish(physical));

        gate.ChangeSource(@"\\.\DISPLAY9");
        Assert.IsFalse(gate.CanPublish(physical));
        var virtualDisplay = gate.Capture();
        Assert.IsTrue(gate.CanPublish(virtualDisplay));

        gate.Stop();
        Assert.IsFalse(gate.CanPublish(virtualDisplay));
    }

    [TestMethod]
    public void PreviewUsesSrccopyAndPersistentPixelBufferWithoutChangingSystemCursor()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "StreamMonitorPage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "StreamMonitorPage.xaml"));
        var capture = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Services",
            "GdiDesktopPreviewService.cs"));
        var frame = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Services",
            "DesktopPreviewFrame.cs"));

        StringAssert.Contains(page, "new WriteableBitmap");
        StringAssert.Contains(page, "PixelBuffer.AsStream()");
        StringAssert.Contains(page, ".Invalidate()");
        Assert.IsFalse(page.Contains("new BitmapImage", StringComparison.Ordinal));
        Assert.AreEqual(1, Count(page, "PreviewImage.Source ="),
            "The XAML image source must stay stable across frames.");
        StringAssert.Contains(xaml, "IsHitTestVisible=\"False\"");
        StringAssert.Contains(capture, "Srccopy))");
        StringAssert.Contains(capture, "gdiSrccopyWithOwnedCursorOverlay");
        Assert.IsFalse(capture.Contains("Captureblt", StringComparison.OrdinalIgnoreCase),
            "CAPTUREBLT forces layered-window composition and can blink the local software cursor.");
        StringAssert.Contains(capture, "GetCursorInfo");
        StringAssert.Contains(capture, "GetIconInfo");
        StringAssert.Contains(capture, "DrawIconEx");
        StringAssert.Contains(capture, "DeleteObject(icon.MaskBitmap)");
        StringAssert.Contains(capture, "DeleteObject(icon.ColorBitmap)");
        Assert.IsFalse(capture.Contains("DestroyIcon", StringComparison.Ordinal));
        foreach (var forbidden in new[]
        {
            "ShowCursor", "SetCursor", "ClipCursor", "SetSystemCursor",
            "SystemParametersInfo", "SetCapture", "ReleaseCapture"
        })
        {
            Assert.IsFalse(capture.Contains(forbidden + "(", StringComparison.Ordinal));
            Assert.IsFalse(page.Contains(forbidden + "(", StringComparison.Ordinal));
        }
        StringAssert.Contains(frame, "int SystemCursorMutationCalls");
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index,
                 StringComparison.Ordinal)) >= 0; index += token.Length) count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "Ligase.Desktop")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootNotFound");
    }
}
