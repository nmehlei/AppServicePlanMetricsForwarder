using Xunit;
using AppServicePlanMetricsForwarder.Configuration;

namespace AppServicePlanMetricsForwarder.Tests;

public class MetricsCollectorTests
{
    private const string TestResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test-rg/providers/Microsoft.Web/serverfarms/test-plan";

    private static ForwarderOptions CreateOptions(string? metricNames = null) => new()
    {
        AppServicePlanResourceId = TestResourceId,
        OtlpEndpoint = "https://localhost",
        MetricNames = metricNames ?? "CpuPercentage,MemoryPercentage"
    };

    [Fact]
    public void GetMetricNamesList_ParsesCommaSeparatedNames()
    {
        var options = CreateOptions("CpuPercentage, MemoryPercentage , DiskQueueLength");

        var names = options.GetMetricNamesList();

        Assert.Equal(3, names.Count);
        Assert.Equal("CpuPercentage", names[0]);
        Assert.Equal("MemoryPercentage", names[1]);
        Assert.Equal("DiskQueueLength", names[2]);
    }

    [Fact]
    public void GetMetricNamesList_DefaultMetrics_ContainsExpectedMetrics()
    {
        var options = CreateOptions(ForwarderOptions.DefaultMetricNames);

        var names = options.GetMetricNamesList();

        Assert.Contains("CpuPercentage", names);
        Assert.Contains("MemoryPercentage", names);
        Assert.Contains("BytesReceived", names);
        Assert.Contains("BytesSent", names);
        Assert.Equal(9, names.Count);
    }
}
