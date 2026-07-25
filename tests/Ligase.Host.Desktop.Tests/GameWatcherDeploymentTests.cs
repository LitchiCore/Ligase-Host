using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class GameWatcherDeploymentTests
{
    private static readonly string ProjectFile = FindRepositoryFile(
        "src",
        "Ligase.Desktop",
        "Ligase.Host.Desktop.csproj");

    [TestMethod]
    public void CopySourceUsesTheCurrentPlatformTargetOutput()
    {
        var project = LoadProject();
        var target = CopyTarget();
        var projectPath = ProjectProperty(project, "GameWatcherProject");
        var getTargetPath = target.Elements().Single(element => element.Name.LocalName == "MSBuild");

        StringAssert.Contains(projectPath, @"tools\Ligase.GameWatcher\Ligase.GameWatcher.csproj");
        Assert.AreEqual("$(GameWatcherProject)", (string?)getTargetPath.Attribute("Projects"));
        Assert.AreEqual("GetTargetPath", (string?)getTargetPath.Attribute("Targets"));
        StringAssert.Contains(
            (string?)getTargetPath.Attribute("Properties"),
            "Configuration=$(Configuration);Platform=$(Platform)");
        Assert.IsFalse(
            target.ToString().Contains(@"bin\$(Configuration)", StringComparison.Ordinal),
            "The legacy output without a platform must never be selected.");
    }

    [TestMethod]
    public void MissingSourceRemovesStaleDeploymentAndFailsBuild()
    {
        var target = CopyTarget();
        var operations = target.Elements().Select(element => element.Name.LocalName).ToArray();
        var deleteIndex = Array.IndexOf(operations, "Delete");
        var errorIndex = Array.IndexOf(operations, "Error");
        var copyIndex = Array.IndexOf(operations, "Copy");

        Assert.IsTrue(deleteIndex >= 0, "Stale deployed GameWatcher files must be removed.");
        Assert.IsTrue(errorIndex > deleteIndex, "Missing source must fail after stale output is removed.");
        Assert.IsTrue(copyIndex > errorIndex, "Copy must only run after the source presence guard.");

        var delete = target.Elements().Single(element => element.Name.LocalName == "Delete");
        var error = target.Elements().Single(element => element.Name.LocalName == "Error");
        StringAssert.Contains((string?)delete.Attribute("Condition"), "$(TargetDir)");
        StringAssert.Contains((string?)error.Attribute("Condition"), "'@(_GameWatcherFiles)' == ''");
    }

    [TestMethod]
    public void CopyDeploysOnlyTheEnumeratedPlatformSourceFiles()
    {
        var target = CopyTarget();
        var sourceItem = target
            .Descendants()
            .Single(element => element.Name.LocalName == "_GameWatcherFiles");
        var copy = target.Elements().Single(element => element.Name.LocalName == "Copy");

        Assert.AreEqual(
            @"$(_GameWatcherSourceDirectory)Ligase.GameWatcher.*",
            (string?)sourceItem.Attribute("Include"));
        Assert.AreEqual("@(_GameWatcherFiles)", (string?)copy.Attribute("SourceFiles"));
        Assert.AreEqual("$(TargetDir)", (string?)copy.Attribute("DestinationFolder"));
        StringAssert.Contains((string?)copy.Attribute("Condition"), "$(TargetDir)");
    }

    [TestMethod]
    public void StructuredPackageDoesNotFlattenWatcherIntoDesktopOutput()
    {
        var condition = (string?)CopyTarget().Attribute("Condition");

        Assert.AreEqual("'$(LigaseStructuredPackage)' != 'true'", condition);
    }

    private static XDocument LoadProject() => XDocument.Load(ProjectFile);

    private static XElement CopyTarget() => LoadProject()
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Target" &&
            string.Equals((string?)element.Attribute("Name"), "CopyGameWatcher", StringComparison.Ordinal));

    private static string ProjectProperty(XDocument project, string name) =>
        project.Descendants().Single(element => element.Name.LocalName == name).Value;

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
