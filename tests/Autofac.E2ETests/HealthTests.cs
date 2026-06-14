namespace Autofac.E2ETests;

public sealed class HealthTests : E2ETestBase
{
    [Fact]
    public async Task Api_IsHealthy()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
        Assert.True(await Api.IsHealthyAsync());
    }
}
