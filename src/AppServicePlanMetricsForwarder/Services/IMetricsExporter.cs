using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public interface IMetricsExporter
{
    Task ExportAsync(IReadOnlyList<MetricDataPoint> dataPoints, CancellationToken cancellationToken = default);
}
