using FluentAssertions;
using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Tests.Domain;

public sealed class ApiKeyTests
{
    private static ApiKey CreateKey() =>
        new("key-1", "user-1", "default", "sk-or-abc", "hash", DateTimeOffset.UtcNow);

    [Fact]
    public void Delete_ActiveKey_Throws()
    {
        var key = CreateKey();

        var act = () => key.Delete(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
        key.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Delete_DisabledKey_MarksDeleted()
    {
        var key = CreateKey();
        var disabledAt = DateTimeOffset.UtcNow;
        key.Disable(disabledAt);

        var deletedAt = disabledAt.AddMinutes(1);
        key.Delete(deletedAt);

        key.IsDeleted.Should().BeTrue();
        key.DeletedAtUtc.Should().Be(deletedAt);
    }

    [Fact]
    public void Delete_AlreadyDeleted_KeepsFirstTimestamp()
    {
        var key = CreateKey();
        var disabledAt = DateTimeOffset.UtcNow;
        key.Disable(disabledAt);
        var firstDelete = disabledAt.AddMinutes(1);
        key.Delete(firstDelete);

        key.Delete(firstDelete.AddMinutes(5));

        key.DeletedAtUtc.Should().Be(firstDelete);
    }
}
