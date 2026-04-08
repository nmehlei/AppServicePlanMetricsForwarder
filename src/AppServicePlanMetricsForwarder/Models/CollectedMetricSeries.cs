using Azure.Monitor.Query.Models;

namespace AppServicePlanMetricsForwarder.Models;

public record CollectedMetricSeries(
    string MetricName,
    IReadOnlyList<CollectedMetricValue> Values);

public record CollectedMetricValue(
    DateTimeOffset Timestamp,
    double? Average,
    double? Count,
    double? Maximum,
    double? Minimum,
    double? Total)
{
    public static CollectedMetricValue FromAzureMetricValue(MetricValue value) =>
        new(
            value.TimeStamp,
            value.Average,
            value.Count,
            value.Maximum,
            value.Minimum,
            value.Total);
}
