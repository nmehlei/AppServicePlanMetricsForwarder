using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public class MetricsCollector(
    MetricsQueryClient metricsQueryClient,
    IOptions<ForwarderOptions> options,
    ILogger<MetricsCollector> logger) : IMetricsCollector
{
    public async Task<IReadOnlyList<MetricDataPoint>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        var metricNames = config.GetMetricNamesList();

        logger.LogInformation("Querying {Count} metrics for {ResourceId}", metricNames.Count, config.AppServicePlanResourceId);

        var response = await metricsQueryClient.QueryResourceAsync(
            config.AppServicePlanResourceId,
            metricNames,
            new MetricsQueryOptions
            {
                Granularity = TimeSpan.FromMinutes(1),
                TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
                Aggregations = { MetricAggregationType.Average }
            },
            cancellationToken);

        var dataPoints = new List<MetricDataPoint>();

        foreach (var metric in response.Value.Metrics)
        {
            // Take the most recent time series element that has a value
            var latestValue = metric.TimeSeries
                .SelectMany(ts => ts.Values)
                .Where(v => v.Average.HasValue)
                .OrderByDescending(v => v.TimeStamp)
                .FirstOrDefault();

            if (latestValue is null)
            {
                logger.LogDebug("No data for metric {MetricName}", metric.Name);
                continue;
            }

            dataPoints.Add(new MetricDataPoint(
                MetricName: metric.Name,
                Value: latestValue.Average!.Value,
                Timestamp: latestValue.TimeStamp,
                ResourceId: config.AppServicePlanResourceId));
        }

        logger.LogInformation("Collected {Count} metric data points", dataPoints.Count);
        return dataPoints;
    }
}
