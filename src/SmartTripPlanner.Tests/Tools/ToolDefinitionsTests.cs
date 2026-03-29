namespace SmartTripPlanner.Tests.Tools;

using SmartTripPlanner.Api.Tools;

public class ToolDefinitionsTests
{
    [Fact]
    public void GetAllTools_ReturnsNineTools()
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Equal(9, tools.Count);
    }

    [Theory]
    [InlineData("get_calendar_view")]
    [InlineData("validate_travel")]
    [InlineData("add_trip_event")]
    [InlineData("search_area")]
    [InlineData("get_weather")]
    [InlineData("delete_trip_event")]
    [InlineData("get_trip")]
    [InlineData("search_restaurants")]
    [InlineData("search_hotels")]
    public void GetAllTools_ContainsTool(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }
}
