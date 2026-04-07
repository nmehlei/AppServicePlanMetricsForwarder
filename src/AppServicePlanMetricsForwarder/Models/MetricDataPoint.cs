namespace AppServicePlanMetricsForwarder.Models;

public record MetricDataPoint(
    string MetricName,
    double Value,
    DateTimeOffset Timestamp,
    string ResourceId,
    string? SiteName = null);
