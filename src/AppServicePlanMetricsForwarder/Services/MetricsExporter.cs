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

    // Plan-level: keyed by MetricName
    private readonly Dictionary<string, MetricDataPoint> _latestPlanValues = new();

    // Site-level: keyed by (MetricName, SiteName)
    private readonly Dictionary<(string MetricName, string SiteName), MetricDataPoint> _latestSiteValues = new();

    // Maps an Azure Monitor plan metric to a host-semantic alias. Scale converts units
    // (e.g. 0-100 percent → 0-1 ratio). Multiple sources may share a destination name;
    // in that case each contributes one Measurement with its own attribute set.
    private readonly record struct HostAlias(string DestName, double Scale, KeyValuePair<string, object?>[] Attrs);

    private static readonly Dictionary<string, HostAlias> HostAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CpuPercentage"] = new("system.cpu.utilization", 0.01,
            new[] { new KeyValuePair<string, object?>("state", "used") }),
        ["MemoryPercentage"] = new("system.memory.utilization", 0.01,
            new[] { new KeyValuePair<string, object?>("state", "used") }),
        ["BytesReceived"] = new("system.network.io", 1.0,
            new[] { new KeyValuePair<string, object?>("direction", "receive") }),
        ["BytesSent"] = new("system.network.io", 1.0,
            new[] { new KeyValuePair<string, object?>("direction", "transmit") }),
        ["TcpEstablished"] = new("system.network.connections", 1.0,
            new[]
            {
                new KeyValuePair<string, object?>("state", "established"),
                new KeyValuePair<string, object?>("protocol", "tcp"),
            }),
        ["TcpTimeWait"] = new("system.network.connections", 1.0,
            new[]
            {
                new KeyValuePair<string, object?>("state", "time_wait"),
                new KeyValuePair<string, object?>("protocol", "tcp"),
            }),
        ["TcpCloseWait"] = new("system.network.connections", 1.0,
            new[]
            {
                new KeyValuePair<string, object?>("state", "close_wait"),
                new KeyValuePair<string, object?>("protocol", "tcp"),
            }),
    };

    public MetricsExporter(IOptions<ForwarderOptions> options, ILogger<MetricsExporter> logger)
    {
        _logger = logger;
        _meter = new Meter(MeterName);

        var config = options.Value;

        var resourceAttrs = new List<KeyValuePair<string, object>>
        {
            new("azure.resource.id", config.AppServicePlanResourceId),
        };

        if (config.EmitAspAsHost)
        {
            var hostName = ExtractAspName(config.AppServicePlanResourceId);
            resourceAttrs.Add(new("host.name", hostName));
            resourceAttrs.Add(new("host.id", config.AppServicePlanResourceId));
            resourceAttrs.Add(new("cloud.provider", "azure"));
            resourceAttrs.Add(new("cloud.platform", "azure_app_service"));
            resourceAttrs.Add(new("cloud.resource_id", config.AppServicePlanResourceId));
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: MeterName)
            .AddAttributes(resourceAttrs);

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

        // Create observable gauges for plan-level metrics
        foreach (var metricName in config.GetMetricNamesList())
        {
            var name = $"azure.app_service_plan.{ToSnakeCase(metricName)}";
            var captured = metricName;
            _meter.CreateObservableGauge(name, () =>
            {
                if (_latestPlanValues.TryGetValue(captured, out var dp))
                {
                    return new Measurement<double>(dp.Value,
                        new KeyValuePair<string, object?>("azure.resource.id", dp.ResourceId));
                }
                return new Measurement<double>();
            });
        }

        // Dual-emit plan metrics under OTel host semantic-convention names so backends
        // like SigNoz render the ASP in their Infrastructure → Hosts view.
        if (config.EmitAspAsHost)
        {
            var enabled = config.GetMetricNamesList()
                .Where(HostAliases.ContainsKey)
                .Select(m => (Source: m, Alias: HostAliases[m]))
                .ToList();

            foreach (var group in enabled.GroupBy(x => x.Alias.DestName))
            {
                var destName = group.Key;
                var sources = group.ToArray();
                _meter.CreateObservableGauge(destName, () =>
                {
                    var measurements = new List<Measurement<double>>(sources.Length);
                    foreach (var (source, alias) in sources)
                    {
                        if (_latestPlanValues.TryGetValue(source, out var dp))
                        {
                            measurements.Add(new Measurement<double>(dp.Value * alias.Scale, alias.Attrs));
                        }
                    }
                    return measurements;
                });
            }
        }

        // Create observable gauges for site-level metrics
        // Each gauge emits one Measurement per site via the callback
        if (config.CollectSiteMetrics)
        {
            foreach (var metricName in config.GetSiteMetricNamesList())
            {
                var name = $"azure.app_service.{ToSnakeCase(metricName)}";
                var captured = metricName;
                _meter.CreateObservableGauge(name, () =>
                {
                    var measurements = new List<Measurement<double>>();
                    foreach (var kvp in _latestSiteValues)
                    {
                        if (kvp.Key.MetricName == captured)
                        {
                            measurements.Add(new Measurement<double>(kvp.Value.Value,
                                new KeyValuePair<string, object?>("site_name", kvp.Key.SiteName),
                                new KeyValuePair<string, object?>("azure.resource.id", kvp.Value.ResourceId)));
                        }
                    }
                    return measurements;
                });
            }
        }
    }

    public Task ExportAsync(IReadOnlyList<MetricDataPoint> dataPoints, CancellationToken cancellationToken = default)
    {
        foreach (var dp in dataPoints)
        {
            if (dp.SiteName is null)
            {
                _latestPlanValues[dp.MetricName] = dp;
            }
            else
            {
                _latestSiteValues[(dp.MetricName, dp.SiteName)] = dp;
            }
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

    internal static string ExtractAspName(string resourceId)
    {
        const string marker = "/serverfarms/";
        var idx = resourceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "unknown-asp";
        var name = resourceId[(idx + marker.Length)..];
        var slash = name.IndexOf('/');
        return slash < 0 ? name : name[..slash];
    }
}
