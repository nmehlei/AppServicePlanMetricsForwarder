using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Models;
using AppServicePlanMetricsForwarder.Services;

namespace AppServicePlanMetricsForwarder.Tests;

public class MetricsCollectorBehaviorTests
{
    private const string PlanResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test-rg/providers/Microsoft.Web/serverfarms/test-plan";
    private const string SiteResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test-rg/providers/Microsoft.Web/sites/test-site";

    private readonly IAzureMetricsClient _metricsClient = Substitute.For<IAzureMetricsClient>();
    private readonly ISiteDiscoveryService _siteDiscoveryService = Substitute.For<ISiteDiscoveryService>();
    private readonly ILogger<MetricsCollector> _logger = Substitute.For<ILogger<MetricsCollector>>();

    [Fact]
    public async Task CollectAsync_UsesConfiguredAggregationForPlanMetrics()
    {
        var collector = CreateCollector(new ForwarderOptions
        {
            AppServicePlanResourceId = PlanResourceId,
            OtlpEndpoint = "https://localhost",
            MetricNames = "CpuPercentage,BytesSent",
            CollectSiteMetrics = false
        });

        var older = DateTimeOffset.Parse("2026-04-08T10:00:00Z");
        var newer = older.AddMinutes(1);

        _metricsClient.QueryResourceAsync(
                PlanResourceId,
                Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "CpuPercentage" })),
                MetricAggregationType.Average,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CollectedMetricSeries>>(
            [
                new CollectedMetricSeries(
                    "CpuPercentage",
                    [
                        new CollectedMetricValue(older, Average: 40, Count: null, Maximum: null, Minimum: null, Total: null),
                        new CollectedMetricValue(newer, Average: 42, Count: null, Maximum: null, Minimum: null, Total: null)
                    ])
            ]));

        _metricsClient.QueryResourceAsync(
                PlanResourceId,
                Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "BytesSent" })),
                MetricAggregationType.Total,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CollectedMetricSeries>>(
            [
                new CollectedMetricSeries(
                    "BytesSent",
                    [
                        new CollectedMetricValue(older, Average: 5, Count: null, Maximum: null, Minimum: null, Total: 100),
                        new CollectedMetricValue(newer, Average: 7, Count: null, Maximum: null, Minimum: null, Total: 150)
                    ])
            ]));

        var results = await collector.CollectAsync();

        Assert.Collection(
            results.OrderBy(r => r.MetricName),
            metric =>
            {
                Assert.Equal("BytesSent", metric.MetricName);
                Assert.Equal(150, metric.Value);
                Assert.Equal(newer, metric.Timestamp);
            },
            metric =>
            {
                Assert.Equal("CpuPercentage", metric.MetricName);
                Assert.Equal(42, metric.Value);
                Assert.Equal(newer, metric.Timestamp);
            });
    }

    [Fact]
    public async Task CollectAsync_CollectsSiteMetricsAcrossAverageAndTotalGroups()
    {
        var collector = CreateCollector(new ForwarderOptions
        {
            AppServicePlanResourceId = PlanResourceId,
            OtlpEndpoint = "https://localhost",
            MetricNames = "CpuPercentage",
            SiteMetricNames = "Requests,HttpResponseTime",
            CollectSiteMetrics = true
        });

        var timestamp = DateTimeOffset.Parse("2026-04-08T10:05:00Z");

        _metricsClient.QueryResourceAsync(
                PlanResourceId,
                Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "CpuPercentage" })),
                MetricAggregationType.Average,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CollectedMetricSeries>>(
            [
                new CollectedMetricSeries(
                    "CpuPercentage",
                    [new CollectedMetricValue(timestamp, Average: 33, Count: null, Maximum: null, Minimum: null, Total: null)])
            ]));

        _siteDiscoveryService.GetSitesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DiscoveredSite>>(
            [
                new DiscoveredSite("test-site", SiteResourceId)
            ]));

        _metricsClient.QueryResourceAsync(
                SiteResourceId,
                Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "Requests" })),
                MetricAggregationType.Total,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CollectedMetricSeries>>(
            [
                new CollectedMetricSeries(
                    "Requests",
                    [new CollectedMetricValue(timestamp, Average: null, Count: null, Maximum: null, Minimum: null, Total: 84)])
            ]));

        _metricsClient.QueryResourceAsync(
                SiteResourceId,
                Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "HttpResponseTime" })),
                MetricAggregationType.Average,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CollectedMetricSeries>>(
            [
                new CollectedMetricSeries(
                    "HttpResponseTime",
                    [new CollectedMetricValue(timestamp, Average: 0.35, Count: null, Maximum: null, Minimum: null, Total: null)])
            ]));

        var results = await collector.CollectAsync();

        Assert.Contains(results, metric => metric.MetricName == "Requests" && metric.Value == 84 && metric.SiteName == "test-site");
        Assert.Contains(results, metric => metric.MetricName == "HttpResponseTime" && metric.Value == 0.35 && metric.SiteName == "test-site");
    }

    private MetricsCollector CreateCollector(ForwarderOptions options) =>
        new(
            _metricsClient,
            _siteDiscoveryService,
            Options.Create(options),
            _logger);
}
