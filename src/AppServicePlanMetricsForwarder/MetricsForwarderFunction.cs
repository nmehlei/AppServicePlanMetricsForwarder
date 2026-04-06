using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AppServicePlanMetricsForwarder.Services;

namespace AppServicePlanMetricsForwarder;

public class MetricsForwarderFunction(
    IMetricsCollector collector,
    IMetricsExporter exporter,
    ILogger<MetricsForwarderFunction> logger)
{
    [Function("MetricsForwarder")]
    public async Task Run(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("MetricsForwarder triggered at {Time}", DateTimeOffset.UtcNow);

        var metrics = await collector.CollectAsync(cancellationToken);

        if (metrics.Count == 0)
        {
            logger.LogWarning("No metrics collected, skipping export");
            return;
        }

        await exporter.ExportAsync(metrics, cancellationToken);

        logger.LogInformation("Successfully forwarded {Count} metrics", metrics.Count);
    }
}
