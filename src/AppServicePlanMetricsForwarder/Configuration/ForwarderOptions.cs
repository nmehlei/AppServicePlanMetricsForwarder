namespace AppServicePlanMetricsForwarder.Configuration;

public class ForwarderOptions
{
    public const string SectionName = "Forwarder";

    public const string DefaultMetricNames =
        "CpuPercentage,MemoryPercentage,DiskQueueLength,HttpQueueLength," +
        "BytesReceived,BytesSent,TcpConnected,TcpTimeWait,TcpCloseWait";

    /// <summary>
    /// Full ARM resource ID of the App Service Plan.
    /// e.g. /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/serverfarms/{name}
    /// </summary>
    public required string AppServicePlanResourceId { get; set; }

    /// <summary>
    /// OTLP endpoint URL (e.g. https://openobserve.example.com/api/default/v1/metrics).
    /// </summary>
    public required string OtlpEndpoint { get; set; }

    /// <summary>
    /// OTLP headers for authentication, formatted as "key=value" pairs separated by commas.
    /// e.g. "Authorization=Basic dXNlcjpwYXNz,X-Custom=value"
    /// </summary>
    public string? OtlpHeaders { get; set; }

    /// <summary>
    /// Comma-separated list of Azure Monitor metric names to collect.
    /// </summary>
    public string MetricNames { get; set; } = DefaultMetricNames;

    public IReadOnlyList<string> GetMetricNamesList() =>
        MetricNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
