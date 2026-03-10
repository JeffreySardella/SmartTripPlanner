namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Services;

public class GoogleCalendarFactoryTests
{
    [Fact]
    public async Task CreateAsync_MissingCredentialFile_ReturnsNull()
    {
        var result = await GoogleCalendarFactory.CreateAsync(
            "nonexistent_file.json", ".tokens_test");

        Assert.Null(result);
    }
}
