using Azure.Monitor.Query.Models;

namespace AppServicePlanMetricsForwarder.Services;

public static class MetricCatalog
{
    private static readonly IReadOnlyDictionary<string, MetricAggregationType> s_planAggregations =
        new Dictionary<string, MetricAggregationType>(StringComparer.OrdinalIgnoreCase)
        {
            ["BytesReceived"] = MetricAggregationType.Total,
            ["BytesSent"] = MetricAggregationType.Total,
            ["CpuPercentage"] = MetricAggregationType.Average,
            ["DiskQueueLength"] = MetricAggregationType.Average,
            ["HttpQueueLength"] = MetricAggregationType.Average,
            ["MemoryPercentage"] = MetricAggregationType.Average,
            ["SocketInboundAll"] = MetricAggregationType.Average,
            ["SocketLoopback"] = MetricAggregationType.Average,
            ["SocketOutboundAll"] = MetricAggregationType.Average,
            ["SocketOutboundEstablished"] = MetricAggregationType.Average,
            ["SocketOutboundTimeWait"] = MetricAggregationType.Average,
            ["TcpCloseWait"] = MetricAggregationType.Average,
            ["TcpClosing"] = MetricAggregationType.Average,
            ["TcpEstablished"] = MetricAggregationType.Average,
            ["TcpFinWait1"] = MetricAggregationType.Average,
            ["TcpFinWait2"] = MetricAggregationType.Average,
            ["TcpLastAck"] = MetricAggregationType.Average,
            ["TcpSynReceived"] = MetricAggregationType.Average,
            ["TcpSynSent"] = MetricAggregationType.Average,
            ["TcpTimeWait"] = MetricAggregationType.Average,
        };

    private static readonly IReadOnlyDictionary<string, MetricAggregationType> s_siteAggregations =
        new Dictionary<string, MetricAggregationType>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppConnections"] = MetricAggregationType.Average,
            ["AverageMemoryWorkingSet"] = MetricAggregationType.Average,
            ["AverageResponseTime"] = MetricAggregationType.Average,
            ["BytesReceived"] = MetricAggregationType.Total,
            ["BytesSent"] = MetricAggregationType.Total,
            ["CpuPercentage"] = MetricAggregationType.Average,
            ["CpuTime"] = MetricAggregationType.Total,
            ["CurrentAssemblies"] = MetricAggregationType.Average,
            ["FunctionExecutionCount"] = MetricAggregationType.Total,
            ["FunctionExecutionUnits"] = MetricAggregationType.Total,
            ["Gen0Collections"] = MetricAggregationType.Total,
            ["Gen1Collections"] = MetricAggregationType.Total,
            ["Gen2Collections"] = MetricAggregationType.Total,
            ["Handles"] = MetricAggregationType.Average,
            ["HealthCheckStatus"] = MetricAggregationType.Average,
            ["Http101"] = MetricAggregationType.Total,
            ["Http2xx"] = MetricAggregationType.Total,
            ["Http3xx"] = MetricAggregationType.Total,
            ["Http401"] = MetricAggregationType.Total,
            ["Http403"] = MetricAggregationType.Total,
            ["Http404"] = MetricAggregationType.Total,
            ["Http406"] = MetricAggregationType.Total,
            ["Http4xx"] = MetricAggregationType.Total,
            ["Http5xx"] = MetricAggregationType.Total,
            ["HttpResponseTime"] = MetricAggregationType.Average,
            ["InstanceCount"] = MetricAggregationType.Average,
            ["IoOtherBytesPerSecond"] = MetricAggregationType.Total,
            ["IoOtherOperationsPerSecond"] = MetricAggregationType.Total,
            ["IoReadBytesPerSecond"] = MetricAggregationType.Total,
            ["IoReadOperationsPerSecond"] = MetricAggregationType.Total,
            ["IoWriteBytesPerSecond"] = MetricAggregationType.Total,
            ["IoWriteOperationsPerSecond"] = MetricAggregationType.Total,
            ["MemoryWorkingSet"] = MetricAggregationType.Average,
            ["PrivateBytes"] = MetricAggregationType.Average,
            ["Requests"] = MetricAggregationType.Total,
            ["RequestsInApplicationQueue"] = MetricAggregationType.Average,
            ["Threads"] = MetricAggregationType.Average,
            ["TotalAppDomains"] = MetricAggregationType.Average,
            ["TotalAppDomainsUnloaded"] = MetricAggregationType.Average,
        };

    public static IReadOnlyDictionary<string, MetricAggregationType> ResolvePlanMetrics(
        IReadOnlyList<string> metricNames) =>
        Resolve(metricNames, s_planAggregations);

    public static IReadOnlyDictionary<string, MetricAggregationType> ResolveSiteMetrics(
        IReadOnlyList<string> metricNames) =>
        Resolve(metricNames, s_siteAggregations);

    private static IReadOnlyDictionary<string, MetricAggregationType> Resolve(
        IReadOnlyList<string> metricNames,
        IReadOnlyDictionary<string, MetricAggregationType> catalog)
    {
        var resolved = new Dictionary<string, MetricAggregationType>(StringComparer.OrdinalIgnoreCase);

        foreach (var metricName in metricNames)
        {
            resolved[metricName] = catalog.TryGetValue(metricName, out var aggregation)
                ? aggregation
                : MetricAggregationType.Average;
        }

        return resolved;
    }
}
