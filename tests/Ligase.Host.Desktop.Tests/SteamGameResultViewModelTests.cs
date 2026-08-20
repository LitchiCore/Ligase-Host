using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class SteamGameResultViewModelTests
{
    [TestMethod]
    public void ReconcileSeparatesAvailableAndAddedWithStableOrdering()
    {
        var results = new SteamGameResultsViewModel();
        var addedId = Guid.NewGuid();

        results.Reconcile(
            [Game(30, "Zulu"), Game(20, "Alpha"), Game(10, "Alpha")],
            [LibraryItem(addedId, 20)]);

        CollectionAssert.AreEqual(
            new uint[] { 10, 30 },
            results.AvailableGames.Select(result => result.AppId).ToArray());
        CollectionAssert.AreEqual(
            new uint[] { 20 },
            results.AddedGames.Select(result => result.AppId).ToArray());
        Assert.AreEqual(addedId, results.AddedGames[0].LibraryItemId);
    }

    [TestMethod]
    public void FilterPreservesMembershipAcrossBothGroups()
    {
        var results = new SteamGameResultsViewModel();
        results.Reconcile(
            [Game(10, "Alpha One"), Game(20, "Alpha Two"), Game(30, "Beta")],
            [LibraryItem(Guid.NewGuid(), 20)]);

        results.ApplyFilter("Alpha");

        CollectionAssert.AreEqual(
            new uint[] { 10 },
            results.AvailableGames.Select(result => result.AppId).ToArray());
        CollectionAssert.AreEqual(
            new uint[] { 20 },
            results.AddedGames.Select(result => result.AppId).ToArray());
    }

    [TestMethod]
    public void SuccessfulMutationMovesOnlyTargetAndKeepsResultIdentity()
    {
        var results = new SteamGameResultsViewModel();
        results.Reconcile([Game(10, "Alpha"), Game(20, "Beta")], []);
        var target = results.AvailableGames[0];
        var untouched = results.AvailableGames[1];
        var libraryId = Guid.NewGuid();

        results.MarkAdded(target, libraryId);

        Assert.AreSame(target, results.AddedGames.Single());
        Assert.AreSame(untouched, results.AvailableGames.Single());
        Assert.AreEqual(libraryId, target.LibraryItemId);

        results.MarkRemoved(target);

        Assert.AreSame(target, results.AvailableGames[0]);
        Assert.IsFalse(target.IsAdded);
    }

    [TestMethod]
    public void SingleFlightAndFailureRollbackKeepOriginalMembership()
    {
        var result = new SteamGameResultViewModel(
            Game(10, "Alpha"),
            canModifyLibrary: true);

        Assert.IsTrue(result.TryBeginMutation());
        Assert.IsFalse(result.TryBeginMutation());
        Assert.IsFalse(result.IsAdded);

        result.EndMutation();

        Assert.IsFalse(result.IsBusy);
        Assert.IsFalse(result.IsAdded);
        Assert.AreEqual("可添加", result.StatusText);
    }

    [TestMethod]
    public void AccessibleNamesBindExactGameAndTrackMutationState()
    {
        var result = new SteamGameResultViewModel(
            Game(42, "Exact Game"),
            canModifyLibrary: true);

        Assert.AreEqual(
            "Exact Game，Steam App ID 42，可添加",
            result.CardAccessibleName);
        Assert.AreEqual("为 Exact Game 选择封面", result.CoverActionAccessibleName);
        Assert.AreEqual("将 Exact Game 添加到 Ligase 游戏库", result.AddActionAccessibleName);
        Assert.AreEqual("从 Ligase 游戏库移除 Exact Game", result.RemoveActionAccessibleName);

        Assert.IsTrue(result.TryBeginMutation());
        Assert.AreEqual(
            "Exact Game，Steam App ID 42，正在添加…",
            result.CardAccessibleName);
    }

    private static SteamGame Game(uint appId, string name) =>
        new(
            appId,
            name,
            name.Replace(' ', '_'),
            @"D:\Steam",
            $@"D:\Steam\steamapps\appmanifest_{appId}.acf",
            1024);

    private static LibraryItem LibraryItem(Guid id, uint appId) =>
        new()
        {
            Id = id,
            Kind = LibraryItemKind.Steam,
            Name = $"Game {appId}",
            SteamAppId = appId
        };
}
