using FluentAssertions;
using OpenRussiaRouter.CollectorService.Application.Collection;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

public class CollectorSnapshotStoreTests
{
    [Fact]
    public void Current_BeforeAnySet_ReturnsEmpty()
    {
        var store = new CollectorSnapshotStore();
        store.Current.Should().BeSameAs(CollectorSnapshot.Empty);
    }

    [Fact]
    public void Set_ThenCurrent_ReturnsLatestSnapshot()
    {
        var store = new CollectorSnapshotStore();
        var snapshot = new CollectorSnapshot(
            [new NodeCatalogResult("n1", false, [], null, null)],
            DateTimeOffset.UnixEpoch);

        store.Set(snapshot);

        store.Current.Should().BeSameAs(snapshot);
    }
}
