using Xunit;
using AppServicePlanMetricsForwarder.Services;

namespace AppServicePlanMetricsForwarder.Tests;

public class MetricsExporterTests
{
    [Theory]
    [InlineData(
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Web/serverfarms/my-plan",
        "my-plan")]
    [InlineData(
        "/subscriptions/xyz/resourceGroups/rg/providers/Microsoft.Web/serverfarms/Plan-With-Dashes",
        "Plan-With-Dashes")]
    [InlineData(
        "/subscriptions/xyz/resourceGroups/rg/providers/Microsoft.Web/serverfarms/plan/extra/segments",
        "plan")]
    public void ExtractAspName_ParsesResourceId(string resourceId, string expected)
    {
        Assert.Equal(expected, MetricsExporter.ExtractAspName(resourceId));
    }

    [Fact]
    public void ExtractAspName_UnknownFormat_ReturnsFallback()
    {
        Assert.Equal("unknown-asp", MetricsExporter.ExtractAspName("/not/a/serverfarm/id"));
    }
}
