# AppServicePlanMetricsForwarder

An Azure Function that collects Azure App Service Plan metrics from Azure Monitor and forwards them to an OpenTelemetry-compatible backend (e.g. OpenObserve) via OTLP/HTTP.

## Architecture

```
Timer Trigger (1 min) → Azure Function (.NET 10) → Azure Monitor Metrics API
                                                  → OTLP/HTTP → OpenObserve
```

## Metrics

The following App Service Plan metrics are collected from Azure Monitor and pushed as OTel gauges via OTLP/HTTP:

| Azure Monitor Name | OTel Metric Name | Description |
|--------------------|-------------------|-------------|
| `CpuPercentage` | `azure.app_service_plan.cpu_percentage` | CPU usage across instances |
| `MemoryPercentage` | `azure.app_service_plan.memory_percentage` | Memory usage across instances |
| `DiskQueueLength` | `azure.app_service_plan.disk_queue_length` | Disk I/O pressure |
| `HttpQueueLength` | `azure.app_service_plan.http_queue_length` | Request queue depth |
| `BytesReceived` | `azure.app_service_plan.bytes_received` | Inbound network throughput |
| `BytesSent` | `azure.app_service_plan.bytes_sent` | Outbound network throughput |
| `TcpEstablished` | `azure.app_service_plan.tcp_established` | Active TCP connections |
| `TcpTimeWait` | `azure.app_service_plan.tcp_time_wait` | TCP connections in TIME_WAIT |
| `TcpCloseWait` | `azure.app_service_plan.tcp_close_wait` | TCP connections in CLOSE_WAIT |

Per-site App Service metrics are also collected by default, including `CpuTime`, `MemoryWorkingSet`, `AverageMemoryWorkingSet`, `Requests`, `BytesReceived`, `BytesSent`, `Http2xx`, `Http4xx`, `Http5xx`, `HttpResponseTime`, `AppConnections`, `PrivateBytes`, `RequestsInApplicationQueue`, `Threads`, and `Handles`.

All metrics include the resource attribute `azure.resource.id` set to the monitored App Service Plan's ARM resource ID. The metric lists are configurable via `Forwarder__MetricNames` and `Forwarder__SiteMetricNames`. Metric queries are grouped by the Azure Monitor aggregation required by each metric so totals such as `Requests` and `BytesSent` are queried correctly.

## Optional: Emit App Service Plan as an OTel "host" (for SigNoz / infra views)

> ⚠️ **Semantic tradeoff — read before enabling.** An App Service Plan is a managed, multi-instance compute resource, not a host. Setting `Forwarder__EmitAspAsHost=true` makes the exporter **pretend** the ASP is a host so that backends like **SigNoz** render it in their *Infrastructure → Hosts* view. It is **opt-in and disabled by default**. If strict OpenTelemetry semantic-convention compliance matters to you, leave this off and build custom dashboards keyed on `azure.resource.id` instead.

When `Forwarder__EmitAspAsHost=true`, the exporter additionally:

1. **Adds host resource attributes** on the exported resource:
   - `host.name` — derived from the ASP's ARM resource ID (the name segment after `/serverfarms/`)
   - `host.id` — the full ARM resource ID
   - `cloud.provider=azure`, `cloud.platform=azure_app_service`, `cloud.resource_id=<ARM id>`
2. **Dual-emits plan metrics** under OpenTelemetry host-semantic-convention names so SigNoz's built-in infra charts render without custom dashboard work:

   | Azure Monitor source | Host-semantic alias | Unit/transform | Attributes |
   |----------------------|---------------------|----------------|------------|
   | `CpuPercentage` | `system.cpu.utilization` | `value / 100` (ratio 0-1) | `state=used` |
   | `MemoryPercentage` | `system.memory.utilization` | `value / 100` (ratio 0-1) | `state=used` |
   | `BytesReceived` | `system.network.io` | bytes | `direction=receive` |
   | `BytesSent` | `system.network.io` | bytes | `direction=transmit` |
   | `TcpEstablished` | `system.network.connections` | count | `state=established`, `protocol=tcp` |
   | `TcpTimeWait` | `system.network.connections` | count | `state=time_wait`, `protocol=tcp` |
   | `TcpCloseWait` | `system.network.connections` | count | `state=close_wait`, `protocol=tcp` |

The original `azure.app_service_plan.*` metrics continue to be emitted alongside these aliases — dual emission is additive, not a replacement. Metrics without a host-convention equivalent (`DiskQueueLength`, `HttpQueueLength`) are not aliased. Host-view panels not backed by ASP-available metrics (load average, disk, processes) will remain empty in SigNoz — this is expected.

Per-site App Service metrics are **not** affected by this flag: each site is not exported as its own host.

## Configuration

All configuration is via environment variables (or Azure Function Application Settings), using the `Forwarder__` prefix:

| Variable | Required | Description |
|----------|----------|-------------|
| `Forwarder__AppServicePlanResourceId` | Yes | Full ARM resource ID of the App Service Plan |
| `Forwarder__OtlpEndpoint` | Yes | OTLP endpoint URL |
| `Forwarder__OtlpHeaders` | No | OTLP auth headers (`key=value` comma-separated) |
| `Forwarder__MetricNames` | No | Comma-separated metric names (has sensible defaults) |
| `Forwarder__CollectSiteMetrics` | No | Enable per-site App Service metric collection (`true` by default) |
| `Forwarder__SiteMetricNames` | No | Comma-separated App Service metric names (has sensible defaults) |
| `Forwarder__EmitAspAsHost` | No | Dual-emit ASP metrics with `host.*` resource attrs + `system.*` semantic aliases so SigNoz shows the ASP as a host. `false` by default. See *Optional: Emit App Service Plan as an OTel "host"* above. |

## Local Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- An Azure subscription with an App Service Plan
- `az login` for local authentication

### Setup

1. Clone the repo
2. Copy `src/AppServicePlanMetricsForwarder/local.settings.json.template` to `local.settings.json` and fill in your values
3. Run:

```bash
dotnet build
cd src/AppServicePlanMetricsForwarder
func start
```

### Running Tests

```bash
dotnet test
```

## Deployment

This repo contains only the application code and CI pipeline. Deployment to Azure is handled via a separate private pipeline (Azure DevOps).

The Azure Function requires:
- A **Consumption Plan** Function App with .NET 10 isolated worker
- A **System-assigned Managed Identity** with **Monitoring Reader** role on the target subscription or resource group

## License

[MIT](LICENSE)
