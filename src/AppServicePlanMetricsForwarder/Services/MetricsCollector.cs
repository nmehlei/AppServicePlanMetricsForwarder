using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public class MetricsCollector(
    MetricsQueryClient metricsQueryClient,
    ISiteDiscoveryService siteDiscoveryService,
    IOptions<ForwarderOptions> options,
    ILogger<MetricsCollector> logger) : IMetricsCollector
{
    private static readonly SemaphoreSlim s_throttle = new(10);
    private HashSet<string>? _knownInvalidPlanMetrics;
    private HashSet<string>? _knownInvalidSiteMetrics;

    public async Task<IReadOnlyList<MetricDataPoint>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        var dataPoints = new List<MetricDataPoint>();

        var planMetrics = await CollectPlanMetricsAsync(config, cancellationToken);
        dataPoints.AddRange(planMetrics);

        if (config.CollectSiteMetrics)
        {
            var siteMetrics = await CollectSiteMetricsAsync(config, cancellationToken);
            dataPoints.AddRange(siteMetrics);
        }

        logger.LogInformation("Collected {Count} metric data points total", dataPoints.Count);
        return dataPoints;
    }

    private async Task<List<MetricDataPoint>> CollectPlanMetricsAsync(
        ForwarderOptions config, CancellationToken cancellationToken)
    {
        var metricNames = config.GetMetricNamesList()
            .Where(m => _knownInvalidPlanMetrics?.Contains(m) != true)
            .ToList();

        logger.LogInformation("Querying {Count} plan metrics for {ResourceId}", metricNames.Count, config.AppServicePlanResourceId);

        var queryOptions = new MetricsQueryOptions
        {
            Granularity = TimeSpan.FromMinutes(1),
            TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
            Aggregations = { MetricAggregationType.Average }
        };

        try
        {
            var response = await metricsQueryClient.QueryResourceAsync(
                config.AppServicePlanResourceId,
                metricNames,
                queryOptions,
                cancellationToken);

            return ExtractDataPoints(response.Value.Metrics, config.AppServicePlanResourceId, siteName: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch plan query failed, falling back to per-metric queries");
        }

        // Fall back to individual queries so one invalid metric doesn't block the rest
        var fallbackDataPoints = new List<MetricDataPoint>();
        foreach (var metricName in metricNames)
        {
            try
            {
                var response = await metricsQueryClient.QueryResourceAsync(
                    config.AppServicePlanResourceId,
                    [metricName],
                    queryOptions,
                    cancellationToken);

                fallbackDataPoints.AddRange(ExtractDataPoints(response.Value.Metrics, config.AppServicePlanResourceId, siteName: null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query plan metric {MetricName}, excluding from future queries", metricName);
                _knownInvalidPlanMetrics ??= [];
                _knownInvalidPlanMetrics.Add(metricName);
            }
        }

        return fallbackDataPoints;
    }

    private async Task<List<MetricDataPoint>> CollectSiteMetricsAsync(
        ForwarderOptions config, CancellationToken cancellationToken)
    {
        var sites = await siteDiscoveryService.GetSitesAsync(cancellationToken);
        var siteMetricNames = config.GetSiteMetricNamesList()
            .Where(m => _knownInvalidSiteMetrics?.Contains(m) != true)
            .ToList();

        logger.LogInformation("Querying {MetricCount} metrics for {SiteCount} sites", siteMetricNames.Count, sites.Count);

        var tasks = sites.Select(site => QuerySiteMetricsAsync(site, siteMetricNames, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.SelectMany(r => r).ToList();
    }

    private async Task<List<MetricDataPoint>> QuerySiteMetricsAsync(
        DiscoveredSite site, List<string> metricNames, CancellationToken cancellationToken)
    {
        await s_throttle.WaitAsync(cancellationToken);
        try
        {
            var response = await metricsQueryClient.QueryResourceAsync(
                site.ResourceId,
                metricNames,
                new MetricsQueryOptions
                {
                    Granularity = TimeSpan.FromMinutes(1),
                    TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
                    Aggregations = { MetricAggregationType.Average }
                },
                cancellationToken);

            return ExtractDataPoints(response.Value.Metrics, site.ResourceId, site.Name);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 400)
        {
            // If a batch query for a site fails due to an invalid metric, try individually
            logger.LogWarning(ex, "Batch query failed for site {SiteName}, falling back to per-metric queries", site.Name);

            var fallback = new List<MetricDataPoint>();
            foreach (var metricName in metricNames)
            {
                try
                {
                    var response = await metricsQueryClient.QueryResourceAsync(
                        site.ResourceId,
                        [metricName],
                        new MetricsQueryOptions
                        {
                            Granularity = TimeSpan.FromMinutes(1),
                            TimeRange = new QueryTimeRange(TimeSpan.FromMinutes(2)),
                            Aggregations = { MetricAggregationType.Average }
                        },
                        cancellationToken);

                    fallback.AddRange(ExtractDataPoints(response.Value.Metrics, site.ResourceId, site.Name));
                }
                catch (Exception innerEx)
                {
                    logger.LogWarning(innerEx, "Failed to query site metric {MetricName} for {SiteName}, excluding from future queries", metricName, site.Name);
                    _knownInvalidSiteMetrics ??= [];
                    _knownInvalidSiteMetrics.Add(metricName);
                }
            }
            return fallback;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query metrics for site {SiteName} ({ResourceId})", site.Name, site.ResourceId);
            return [];
        }
        finally
        {
            s_throttle.Release();
        }
    }

    private List<MetricDataPoint> ExtractDataPoints(
        IReadOnlyList<MetricResult> metrics, string resourceId, string? siteName)
    {
        var dataPoints = new List<MetricDataPoint>();
        foreach (var metric in metrics)
        {
            var latestValue = metric.TimeSeries
                .SelectMany(ts => ts.Values)
                .Where(v => v.Average.HasValue)
                .OrderByDescending(v => v.TimeStamp)
                .FirstOrDefault();

            if (latestValue is null)
            {
                logger.LogDebug("No data for metric {MetricName} on {ResourceId}", metric.Name, resourceId);
                continue;
            }

            dataPoints.Add(new MetricDataPoint(
                MetricName: metric.Name,
                Value: latestValue.Average!.Value,
                Timestamp: latestValue.TimeStamp,
                ResourceId: resourceId,
                SiteName: siteName));
        }
        return dataPoints;
    }
}
