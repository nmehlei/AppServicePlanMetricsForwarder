using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.OpenTelemetry;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Services;

// Bootstrap logger: captures startup crashes before the host and OTLP sink are configured
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = FunctionsApplication.CreateBuilder(args);

    builder.Services
        .AddOptions<ForwarderOptions>()
        .BindConfiguration(ForwarderOptions.SectionName);

    // Reconfigure Serilog now that configuration is available
    var otlpEndpoint = builder.Configuration[$"{ForwarderOptions.SectionName}:OtlpEndpoint"] ?? "";
    var otlpHeadersRaw = builder.Configuration[$"{ForwarderOptions.SectionName}:OtlpHeaders"] ?? "";

    var logsEndpoint = otlpEndpoint.TrimEnd('/') + "/v1/logs";

    var otlpHeaders = otlpHeadersRaw
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(h => h.Split('=', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = logsEndpoint;
            options.Protocol = OtlpProtocol.HttpProtobuf;
            options.Headers = otlpHeaders;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "AppServicePlanMetricsForwarder"
            };
        })
        .CreateLogger();

    builder.Logging.AddSerilog(Log.Logger, dispose: true);

    builder.Services.AddSingleton(new DefaultAzureCredential());
    builder.Services.AddSingleton(sp =>
        new MetricsQueryClient(sp.GetRequiredService<DefaultAzureCredential>()));

    builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
    builder.Services.AddSingleton<IMetricsExporter, MetricsExporter>();

    var app = builder.Build();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
