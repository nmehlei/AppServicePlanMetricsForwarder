# AppServicePlanMetricsForwarder

An Azure Function that collects Azure App Service Plan metrics from Azure Monitor and forwards them to an OpenTelemetry-compatible backend (e.g. OpenObserve) via OTLP/HTTP.

## Architecture

```
Timer Trigger (1 min) → Azure Function (.NET 10) → Azure Monitor Metrics API
                                                  → OTLP/HTTP → OpenObserve
```

## Metrics

The following metrics are collected from Azure Monitor and pushed as OTel gauges via OTLP/HTTP:

| Azure Monitor Name | OTel Metric Name | Description |
|--------------------|-------------------|-------------|
| `CpuPercentage` | `azure.app_service_plan.cpu_percentage` | CPU usage across instances |
| `MemoryPercentage` | `azure.app_service_plan.memory_percentage` | Memory usage across instances |
| `DiskQueueLength` | `azure.app_service_plan.disk_queue_length` | Disk I/O pressure |
| `HttpQueueLength` | `azure.app_service_plan.http_queue_length` | Request queue depth |
| `BytesReceived` | `azure.app_service_plan.bytes_received` | Inbound network throughput |
| `BytesSent` | `azure.app_service_plan.bytes_sent` | Outbound network throughput |
| `TcpConnected` | `azure.app_service_plan.tcp_connected` | Active TCP connections |
| `TcpTimeWait` | `azure.app_service_plan.tcp_time_wait` | TCP connections in TIME_WAIT |
| `TcpCloseWait` | `azure.app_service_plan.tcp_close_wait` | TCP connections in CLOSE_WAIT |

All metrics include the resource attribute `azure.resource.id` set to the monitored App Service Plan's ARM resource ID. The metric list is configurable via `Forwarder__MetricNames`.

## Configuration

All configuration is via environment variables (or Azure Function Application Settings), using the `Forwarder__` prefix:

| Variable | Required | Description |
|----------|----------|-------------|
| `Forwarder__AppServicePlanResourceId` | Yes | Full ARM resource ID of the App Service Plan |
| `Forwarder__OtlpEndpoint` | Yes | OTLP endpoint URL |
| `Forwarder__OtlpHeaders` | No | OTLP auth headers (`key=value` comma-separated) |
| `Forwarder__MetricNames` | No | Comma-separated metric names (has sensible defaults) |

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
