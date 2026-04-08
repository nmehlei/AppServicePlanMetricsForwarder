using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public class AzureMetricsClient(MetricsQueryClient metricsQueryClient) : IAzureMetricsClient
{
    public async Task<IReadOnlyList<CollectedMetricSeries>> QueryResourceAsync(
        string resourceId,
        IReadOnlyList<string> metricNames,
        MetricAggregationType aggregation,
        CancellationToken cancellationToken = default)
    {
        var response = await metricsQueryClient.QueryResourceAsync(
            resourceId,
            metricNames,
            new MetricsQueryOptions
            {
                Granularity = TimeSpan.FromMinutes(1),
                TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
                Aggregations = { aggregation }
            },
            cancellationToken);

        return response.Value.Metrics
            .Select(metric => new CollectedMetricSeries(
                metric.Name,
                metric.TimeSeries
                    .SelectMany(series => series.Values)
                    .Select(CollectedMetricValue.FromAzureMetricValue)
                    .ToList()))
            .ToList();
    }
}
