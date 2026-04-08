using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppServicePlanMetricsForwarder.Configuration;
using AppServicePlanMetricsForwarder.Models;

namespace AppServicePlanMetricsForwarder.Services;

public class MetricsCollector(
    IAzureMetricsClient metricsClient,
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
        var metrics = MetricCatalog.ResolvePlanMetrics(config.GetMetricNamesList())
            .Where(m => _knownInvalidPlanMetrics?.Contains(m.Key) != true)
            .ToDictionary(m => m.Key, m => m.Value, StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Querying {Count} plan metrics for {ResourceId}", metrics.Count, config.AppServicePlanResourceId);

        try
        {
            return await QueryMetricGroupsAsync(
                config.AppServicePlanResourceId,
                siteName: null,
                metrics,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch plan query failed, falling back to per-metric queries");
        }

        // Fall back to individual queries so one invalid metric doesn't block the rest
        var fallbackDataPoints = new List<MetricDataPoint>();
        foreach (var metric in metrics)
        {
            try
            {
                var response = await metricsClient.QueryResourceAsync(
                    config.AppServicePlanResourceId,
                    [metric.Key],
                    metric.Value,
                    cancellationToken);

                fallbackDataPoints.AddRange(ExtractDataPoints(response, config.AppServicePlanResourceId, siteName: null, metrics));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query plan metric {MetricName}, excluding from future queries", metric.Key);
                _knownInvalidPlanMetrics ??= [];
                _knownInvalidPlanMetrics.Add(metric.Key);
            }
        }

        return fallbackDataPoints;
    }

    private async Task<List<MetricDataPoint>> CollectSiteMetricsAsync(
        ForwarderOptions config, CancellationToken cancellationToken)
    {
        var sites = await siteDiscoveryService.GetSitesAsync(cancellationToken);
        var siteMetrics = MetricCatalog.ResolveSiteMetrics(config.GetSiteMetricNamesList())
            .Where(m => _knownInvalidSiteMetrics?.Contains(m.Key) != true)
            .ToDictionary(m => m.Key, m => m.Value, StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Querying {MetricCount} metrics for {SiteCount} sites", siteMetrics.Count, sites.Count);

        var tasks = sites.Select(site => QuerySiteMetricsAsync(site, siteMetrics, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.SelectMany(r => r).ToList();
    }

    private async Task<List<MetricDataPoint>> QuerySiteMetricsAsync(
        DiscoveredSite site,
        IReadOnlyDictionary<string, MetricAggregationType> metrics,
        CancellationToken cancellationToken)
    {
        await s_throttle.WaitAsync(cancellationToken);
        try
        {
            return await QueryMetricGroupsAsync(
                site.ResourceId,
                site.Name,
                metrics,
                cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 400)
        {
            // If a batch query for a site fails due to an invalid metric, try individually
            logger.LogWarning(ex, "Batch query failed for site {SiteName}, falling back to per-metric queries", site.Name);

            var fallback = new List<MetricDataPoint>();
            foreach (var metric in metrics)
            {
                try
                {
                    var response = await metricsClient.QueryResourceAsync(
                        site.ResourceId,
                        [metric.Key],
                        metric.Value,
                        cancellationToken);

                    fallback.AddRange(ExtractDataPoints(response, site.ResourceId, site.Name, metrics));
                }
                catch (Exception innerEx)
                {
                    logger.LogWarning(innerEx, "Failed to query site metric {MetricName} for {SiteName}, excluding from future queries", metric.Key, site.Name);
                    _knownInvalidSiteMetrics ??= [];
                    _knownInvalidSiteMetrics.Add(metric.Key);
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

    private async Task<List<MetricDataPoint>> QueryMetricGroupsAsync(
        string resourceId,
        string? siteName,
        IReadOnlyDictionary<string, MetricAggregationType> metrics,
        CancellationToken cancellationToken)
    {
        var dataPoints = new List<MetricDataPoint>();

        foreach (var group in metrics.GroupBy(m => m.Value))
        {
            var metricNames = group.Select(m => m.Key).ToList();
            var response = await metricsClient.QueryResourceAsync(
                resourceId,
                metricNames,
                group.Key,
                cancellationToken);

            dataPoints.AddRange(ExtractDataPoints(response, resourceId, siteName, metrics));
        }

        return dataPoints;
    }

    private List<MetricDataPoint> ExtractDataPoints(
        IReadOnlyList<CollectedMetricSeries> metrics,
        string resourceId,
        string? siteName,
        IReadOnlyDictionary<string, MetricAggregationType> aggregations)
    {
        var dataPoints = new List<MetricDataPoint>();
        foreach (var metric in metrics)
        {
            if (!aggregations.TryGetValue(metric.MetricName, out var aggregation))
            {
                logger.LogDebug("Skipping uncataloged metric {MetricName} on {ResourceId}", metric.MetricName, resourceId);
                continue;
            }

            var latestValue = metric.Values
                .Select(v => new
                {
                    v.Timestamp,
                    Value = GetValueForAggregation(v, aggregation)
                })
                .Where(v => v.Value.HasValue)
                .OrderByDescending(v => v.Timestamp)
                .FirstOrDefault();

            if (latestValue is null)
            {
                logger.LogDebug("No {Aggregation} data for metric {MetricName} on {ResourceId}", aggregation, metric.MetricName, resourceId);
                continue;
            }

            dataPoints.Add(new MetricDataPoint(
                MetricName: metric.MetricName,
                Value: latestValue.Value!.Value,
                Timestamp: latestValue.Timestamp,
                ResourceId: resourceId,
                SiteName: siteName));
        }
        return dataPoints;
    }

    private static double? GetValueForAggregation(CollectedMetricValue value, MetricAggregationType aggregation) =>
        aggregation switch
        {
            MetricAggregationType.Average => value.Average,
            MetricAggregationType.Count => value.Count,
            MetricAggregationType.Maximum => value.Maximum,
            MetricAggregationType.Minimum => value.Minimum,
            MetricAggregationType.Total => value.Total,
            _ => value.Average
        };
}
