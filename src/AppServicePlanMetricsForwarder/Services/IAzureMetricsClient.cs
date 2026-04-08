using Azure.Monitor.Query.Models;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public interface IAzureMetricsClient
{
    Task<IReadOnlyList<CollectedMetricSeries>> QueryResourceAsync(
        string resourceId,
        IReadOnlyList<string> metricNames,
        MetricAggregationType aggregation,
        CancellationToken cancellationToken = default);
}
