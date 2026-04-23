namespace AppServicePlanMetricsForwarder.Configuration;

public class ForwarderOptions
{
    public const string SectionName = "Forwarder";

    public const string DefaultMetricNames =
        "CpuPercentage,MemoryPercentage,DiskQueueLength,HttpQueueLength," +
        "BytesReceived,BytesSent,TcpEstablished,TcpTimeWait,TcpCloseWait";

    public const string DefaultSiteMetricNames =
        "CpuTime,MemoryWorkingSet,AverageMemoryWorkingSet,Requests," +
        "BytesReceived,BytesSent,Http2xx,Http4xx,Http5xx,HttpResponseTime," +
        "AppConnections,PrivateBytes,RequestsInApplicationQueue,Threads,Handles";

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
    /// Comma-separated list of Azure Monitor metric names to collect from the App Service Plan.
    /// </summary>
    public string MetricNames { get; set; } = DefaultMetricNames;

    /// <summary>
    /// Whether to collect per-site metrics from individual App Services within the plan.
    /// </summary>
    public bool CollectSiteMetrics { get; set; } = true;

    /// <summary>
    /// Comma-separated list of Azure Monitor metric names to collect from each App Service site.
    /// </summary>
    public string SiteMetricNames { get; set; } = DefaultSiteMetricNames;

    /// <summary>
    /// How often (in minutes) to re-discover App Service sites within the plan.
    /// </summary>
    public int SiteDiscoveryIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// When true, additionally exports the App Service Plan as an OpenTelemetry "host"
    /// (host.name / host.id resource attributes + system.* semantic metric aliases) so that
    /// backends like SigNoz surface the ASP in their Infrastructure → Hosts view.
    /// Semantically this treats the ASP as a host — see README for the tradeoff. Off by default.
    /// </summary>
    public bool EmitAspAsHost { get; set; } = false;

    public IReadOnlyList<string> GetMetricNamesList() =>
        MetricNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> GetSiteMetricNamesList() =>
        SiteMetricNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
