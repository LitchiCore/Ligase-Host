using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class SteamGameSearchTests
{
    private static readonly SteamGame Game = new(
        236390,
        "War Thunder",
        "War Thunder",
        @"D:\SteamLibrary",
        @"D:\SteamLibrary\steamapps\appmanifest_236390.acf",
        100);

    [DataTestMethod]
    [DataRow("war")]
    [DataRow("236390")]
    [DataRow("SteamLibrary")]
    [DataRow("  thunder  ")]
    [DataRow("")]
    public void Matches_FindsSupportedQueries(string query)
    {
        Assert.IsTrue(SteamGameSearch.Matches(Game, query));
    }

    [TestMethod]
    public void Matches_RejectsUnrelatedQuery()
    {
        Assert.IsFalse(SteamGameSearch.Matches(Game, "Stray"));
    }
}
