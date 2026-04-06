using Xunit;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using AppServicePlanMetricsForwarder.Models;
using AppServicePlanMetricsForwarder.Services;

namespace AppServicePlanMetricsForwarder.Tests;

public class MetricsForwarderFunctionTests
{
    private const string TestResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test-rg/providers/Microsoft.Web/serverfarms/test-plan";

    private readonly IMetricsCollector _collector = Substitute.For<IMetricsCollector>();
    private readonly IMetricsExporter _exporter = Substitute.For<IMetricsExporter>();
    private readonly ILogger<MetricsForwarderFunction> _logger = Substitute.For<ILogger<MetricsForwarderFunction>>();
    private readonly MetricsForwarderFunction _function;

    public MetricsForwarderFunctionTests()
    {
        _function = new MetricsForwarderFunction(_collector, _exporter, _logger);
    }

    [Fact]
    public async Task Run_WithMetrics_CallsExporter()
    {
        var metrics = new List<MetricDataPoint>
        {
            new("CpuPercentage", 42.5, DateTimeOffset.UtcNow, TestResourceId),
            new("MemoryPercentage", 67.3, DateTimeOffset.UtcNow, TestResourceId),
        };
        _collector.CollectAsync(Arg.Any<CancellationToken>()).Returns(metrics);
        var timer = Substitute.For<TimerInfo>();

        await _function.Run(timer, CancellationToken.None);

        await _exporter.Received(1).ExportAsync(
            Arg.Is<IReadOnlyList<MetricDataPoint>>(m => m.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WithNoMetrics_SkipsExport()
    {
        _collector.CollectAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MetricDataPoint>());
        var timer = Substitute.For<TimerInfo>();

        await _function.Run(timer, CancellationToken.None);

        await _exporter.DidNotReceive().ExportAsync(
            Arg.Any<IReadOnlyList<MetricDataPoint>>(),
            Arg.Any<CancellationToken>());
    }
}
