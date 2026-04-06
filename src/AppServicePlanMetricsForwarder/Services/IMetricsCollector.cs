using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public interface IMetricsCollector
{
    Task<IReadOnlyList<MetricDataPoint>> CollectAsync(CancellationToken cancellationToken = default);
}
