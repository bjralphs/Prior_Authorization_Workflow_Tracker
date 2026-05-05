using Tests.Unit.Fakes;

namespace Tests.Unit.Fakes;

/// <summary>Smoke tests for the test infrastructure fakes themselves.</summary>
public class FakeInfrastructureTests
{
    [Fact]
    public void FakeDateTimeProvider_ReturnsFixedNow()
    {
        var fixed_ = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeDateTimeProvider(fixed_);

        Assert.Equal(fixed_, clock.UtcNow);
    }

    [Fact]
    public void FakeDateTimeProvider_CanBeAdvanced()
    {
        var clock = new FakeDateTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        clock.UtcNow = clock.UtcNow.AddDays(30);

        Assert.Equal(new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), clock.UtcNow);
    }

    [Theory]
    [InlineData("Authorization Specialist", true)]
    [InlineData("Payer Reviewer", false)]
    public void FakeCurrentUserService_AsSpecialist_HasCorrectRole(string role, bool expected)
    {
        var user = FakeCurrentUserService.AsSpecialist();
        Assert.Equal(expected, user.IsInRole(role));
    }

    [Fact]
    public void FakeCurrentUserService_IsActiveByDefault()
    {
        var user = FakeCurrentUserService.AsSpecialist();
        Assert.True(user.IsActive);
    }
}
