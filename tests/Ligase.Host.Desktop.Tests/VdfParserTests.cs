using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class VdfParserTests
{
    [TestMethod]
    public void Parse_ReadsNestedObjectsAndEscapedPaths()
    {
        const string content =
            """
            "libraryfolders"
            {
                "0"
                {
                    "path" "C:\\Program Files (x86)\\Steam"
                    "label" ""
                }
                // Libraries may live on another drive.
                "1" "D:\\SteamLibrary"
            }
            """;

        var folders = VdfParser.Parse(content).GetObject("libraryfolders");

        Assert.IsNotNull(folders);
        Assert.AreEqual(@"C:\Program Files (x86)\Steam", folders.GetObject("0")?.GetString("path"));
        Assert.AreEqual(@"D:\SteamLibrary", folders.GetString("1"));
    }

    [TestMethod]
    public void Parse_ReadsAppManifest()
    {
        const string content =
            """
            "AppState"
            {
                "appid" "1245620"
                "name" "ELDEN RING"
                "installdir" "ELDEN RING"
                "SizeOnDisk" "52798414848"
            }
            """;

        var state = VdfParser.Parse(content).GetObject("AppState");

        Assert.IsNotNull(state);
        Assert.AreEqual("1245620", state.GetString("appid"));
        Assert.AreEqual("ELDEN RING", state.GetString("name"));
    }

    [TestMethod]
    public void Parse_RejectsUnclosedObjects()
    {
        Assert.ThrowsException<FormatException>(() => VdfParser.Parse("\"root\" { \"key\" \"value\""));
    }
}
