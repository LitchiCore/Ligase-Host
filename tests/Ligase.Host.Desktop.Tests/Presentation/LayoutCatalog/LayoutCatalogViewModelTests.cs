using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.Presentation.LayoutCatalog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests.Presentation.LayoutCatalog;

[TestClass]
public sealed class LayoutCatalogViewModelTests
{
    [TestMethod]
    public async Task LoadProjectsTypedDescriptorAndBindingAvailability()
    {
        var published = Descriptor(
            "10000000-0000-0000-0000-000000000000",
            2,
            "published");
        var retired = Descriptor(
            "20000000-0000-0000-0000-000000000000",
            3,
            "retired");
        var service = new LayoutCatalogService(new MemoryRepository(
            new LayoutCatalogSnapshot(
                1,
                [published, retired],
                [
                    Binding(published.LayoutId, published.Revision),
                    Binding(retired.LayoutId, retired.Revision, "70000000-0000-0000-0000-000000000000"),
                    Binding("30000000-0000-0000-0000-000000000000", 1, "80000000-0000-0000-0000-000000000000")
                ])));
        var viewModel = new LayoutCatalogViewModel(service);

        await viewModel.LoadAsync();

        Assert.AreEqual(2, viewModel.Items.Count);
        Assert.AreEqual("已发布 · published", viewModel.Items[0].PublicationStatusLabel);
        StringAssert.Contains(viewModel.Items[0].VariantSummary, "phone/tablet");
        StringAssert.Contains(viewModel.Items[0].BindingSummary, "可用 1");
        Assert.AreEqual(2, viewModel.BindingAlerts.Count);
        CollectionAssert.AreEqual(
            new[] { "目标已停用", "目标缺失" },
            viewModel.BindingAlerts.Select(alert => alert.StatusLabel).ToArray());
    }

    [TestMethod]
    public async Task EmptyCatalogProducesExecutableEmptyState()
    {
        var viewModel = new LayoutCatalogViewModel(
            new LayoutCatalogService(new MemoryRepository(
                new LayoutCatalogSnapshot(1, [], []))));

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.IsEmpty);
        Assert.IsFalse(viewModel.HasError);
        Assert.IsNull(viewModel.SelectedItem);
    }

    [TestMethod]
    public async Task ReadFailureProducesNaturalLanguageRecoveryWithoutFakeItems()
    {
        var viewModel = new LayoutCatalogViewModel(
            new LayoutCatalogService(new ThrowingRepository()));

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual(0, viewModel.Items.Count);
        StringAssert.Contains(viewModel.ErrorMessage, "请检查本机目录文件后重试");
        StringAssert.Contains(viewModel.ErrorMessage, LayoutCatalogCodes.InvalidJson);
    }

    [TestMethod]
    public async Task ProductBindingUsesExactHostAndGameUuidAndRejectsUnavailableTarget()
    {
        var published = Descriptor(
            "10000000-0000-0000-0000-000000000000",
            2,
            "published");
        var repository = new RecordingRepository(
            new LayoutCatalogSnapshot(1, [published], []));
        var service = new LayoutCatalogService(repository);
        var appId = Guid.Parse("60000000-0000-0000-0000-000000000000");

        await service.SetBindingAsync(
            "50000000-0000-0000-0000-000000000000",
            appId,
            new LayoutBindingV1(published.LayoutId, published.Revision));

        var saved = repository.Snapshot.ExplicitBindings.Single();
        Assert.AreEqual(appId.ToString("D"), saved.Instance.AppUuid);
        Assert.AreEqual(published.LayoutId, saved.Binding.LayoutId);

        await service.SetBindingAsync(
            "50000000-0000-0000-0000-000000000000",
            appId,
            null);
        Assert.AreEqual(0, repository.Snapshot.ExplicitBindings.Count);

        var exception = await Assert.ThrowsExceptionAsync<LayoutCatalogException>(() =>
            service.SetBindingAsync(
                "50000000-0000-0000-0000-000000000000",
                appId,
                new LayoutBindingV1(
                    "90000000-0000-0000-0000-000000000000",
                    1)));
        Assert.AreEqual(LayoutCatalogCodes.InvalidBinding, exception.Code);
        Assert.AreEqual("targetUnavailable", exception.Detail);
    }

    private static LayoutDescriptorV1 Descriptor(
        string layoutId,
        long revision,
        string status) =>
        new(
            1,
            layoutId,
            revision,
            [new PortableGameIdentityV1("steam", "3548580")],
            new LayoutCompatibilityV1(1, 2),
            status,
            [
                new LayoutVariantV1(
                    "40000000-0000-0000-0000-000000000000",
                    "touch",
                    ["phone", "tablet"],
                    ["landscape"])
            ]);

    private static LayoutCatalogBinding Binding(
        string layoutId,
        long revision,
        string appUuid = "60000000-0000-0000-0000-000000000000") =>
        new(
            new LayoutCatalogInstanceIdentity(
                "50000000-0000-0000-0000-000000000000",
                appUuid),
            new LayoutBindingV1(layoutId, revision));

    private sealed class MemoryRepository(LayoutCatalogSnapshot snapshot)
        : ILayoutCatalogRepository
    {
        public Task<LayoutCatalogSnapshot> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveAsync(
            LayoutCatalogSnapshot value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingRepository : ILayoutCatalogRepository
    {
        public Task<LayoutCatalogSnapshot> LoadAsync(
            CancellationToken cancellationToken = default) =>
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidJson);

        public Task SaveAsync(
            LayoutCatalogSnapshot value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRepository(LayoutCatalogSnapshot snapshot)
        : ILayoutCatalogRepository
    {
        public LayoutCatalogSnapshot Snapshot { get; private set; } = snapshot;

        public Task<LayoutCatalogSnapshot> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);

        public Task SaveAsync(
            LayoutCatalogSnapshot value,
            CancellationToken cancellationToken = default)
        {
            Snapshot = value;
            return Task.CompletedTask;
        }
    }
}
