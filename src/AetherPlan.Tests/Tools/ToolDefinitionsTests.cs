namespace AetherPlan.Tests.Tools;

using AetherPlan.Api.Tools;

public class ToolDefinitionsTests
{
    [Fact]
    public void GetAllTools_ReturnsFourTools()
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Equal(4, tools.Count);
    }

    [Theory]
    [InlineData("get_calendar_view")]
    [InlineData("validate_travel")]
    [InlineData("add_trip_event")]
    [InlineData("search_area")]
    public void GetAllTools_ContainsTool(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }
}
