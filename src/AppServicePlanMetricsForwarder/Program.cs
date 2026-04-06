using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ForwarderOptions>()
    .BindConfiguration(ForwarderOptions.SectionName);

builder.Services.AddSingleton(new DefaultAzureCredential());
builder.Services.AddSingleton(sp =>
    new MetricsQueryClient(sp.GetRequiredService<DefaultAzureCredential>()));

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddSingleton<IMetricsExporter, MetricsExporter>();

var app = builder.Build();
app.Run();
