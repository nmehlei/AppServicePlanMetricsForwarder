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
    private HashSet<string>? _knownInvalidMetrics;

    public async Task<IReadOnlyList<MetricDataPoint>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        var metricNames = config.GetMetricNamesList()
            .Where(m => _knownInvalidMetrics?.Contains(m) != true)
            .ToList();

        logger.LogInformation("Querying {Count} metrics for {ResourceId}", metricNames.Count, config.AppServicePlanResourceId);

        var queryOptions = new MetricsQueryOptions
        {
            Granularity = TimeSpan.FromMinutes(1),
            TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
            Aggregations = { MetricAggregationType.Average }
        };

        try
        {
            var response = await metricsQueryClient.QueryResourceAsync(
                config.AppServicePlanResourceId,
                metricNames,
                queryOptions,
                cancellationToken);

            var dataPoints = ExtractDataPoints(response.Value.Metrics, config.AppServicePlanResourceId);
            logger.LogInformation("Collected {Count} metric data points", dataPoints.Count);
            return dataPoints;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch query failed, falling back to per-metric queries");
        }

        // Fall back to individual queries so one invalid metric doesn't block the rest
        var fallbackDataPoints = new List<MetricDataPoint>();
        foreach (var metricName in metricNames)
        {
            try
            {
                var response = await metricsQueryClient.QueryResourceAsync(
                    config.AppServicePlanResourceId,
                    [metricName],
                    queryOptions,
                    cancellationToken);

                fallbackDataPoints.AddRange(ExtractDataPoints(response.Value.Metrics, config.AppServicePlanResourceId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query metric {MetricName}, excluding from future queries", metricName);
                _knownInvalidMetrics ??= [];
                _knownInvalidMetrics.Add(metricName);
            }
        }

        logger.LogInformation("Collected {Count}/{Total} metric data points", fallbackDataPoints.Count, metricNames.Count);
        return fallbackDataPoints;
    }

    private List<MetricDataPoint> ExtractDataPoints(IReadOnlyList<MetricResult> metrics, string resourceId)
    {
        var dataPoints = new List<MetricDataPoint>();
        foreach (var metric in metrics)
        {
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
                ResourceId: resourceId));
        }
        return dataPoints;
    }
}
