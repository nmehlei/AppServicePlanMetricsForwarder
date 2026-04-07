using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public class MetricsExporter : IMetricsExporter, IDisposable
{
    private const string MeterName = "AppServicePlanMetricsForwarder";
    private readonly Meter _meter;
    private readonly MeterProvider _meterProvider;
    private readonly ILogger<MetricsExporter> _logger;

    // Holds the latest values to be reported by observable gauges
    private readonly Dictionary<string, MetricDataPoint> _latestValues = new();

    public MetricsExporter(IOptions<ForwarderOptions> options, ILogger<MetricsExporter> logger)
    {
        _logger = logger;
        _meter = new Meter(MeterName);

        var config = options.Value;

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: MeterName)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("azure.resource.id", config.AppServicePlanResourceId),
            });

        var builder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(MeterName)
            .AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(config.OtlpEndpoint.TrimEnd('/') + "/v1/metrics");
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;

                if (!string.IsNullOrEmpty(config.OtlpHeaders))
                {
                    otlp.Headers = config.OtlpHeaders;
                }
            });

        _meterProvider = builder.Build()!;

        // Create observable gauges for each configured metric
        foreach (var metricName in config.GetMetricNamesList())
        {
            var name = $"azure.app_service_plan.{ToSnakeCase(metricName)}";
            var captured = metricName;
            _meter.CreateObservableGauge(name, () =>
            {
                if (_latestValues.TryGetValue(captured, out var dp))
                {
                    return new Measurement<double>(dp.Value,
                        new KeyValuePair<string, object?>("azure.resource.id", dp.ResourceId));
                }
                return new Measurement<double>();
            });
        }
    }

    public Task ExportAsync(IReadOnlyList<MetricDataPoint> dataPoints, CancellationToken cancellationToken = default)
    {
        foreach (var dp in dataPoints)
        {
            _latestValues[dp.MetricName] = dp;
        }

        _logger.LogInformation("Exporting {Count} metrics via OTLP", dataPoints.Count);

        // Force a collect + export cycle
        if (!_meterProvider.ForceFlush())
            _logger.LogWarning("OTLP export did not complete successfully — metrics may not have been delivered");

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _meter.Dispose();
        _meterProvider.Dispose();
    }

    private static string ToSnakeCase(string input)
    {
        return string.Concat(
            input.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
