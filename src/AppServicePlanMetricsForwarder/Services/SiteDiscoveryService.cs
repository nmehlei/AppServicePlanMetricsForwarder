using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppServicePlanMetricsForwarder.Configuration;

namespace AppServicePlanMetricsForwarder.Services;

public class SiteDiscoveryService(
    ArmClient armClient,
    IOptions<ForwarderOptions> options,
    ILogger<SiteDiscoveryService> logger) : ISiteDiscoveryService
{
    private IReadOnlyList<DiscoveredSite>? _cachedSites;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<DiscoveredSite>> GetSitesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedSites is not null && DateTimeOffset.UtcNow < _cacheExpiry)
            return _cachedSites;

        var config = options.Value;
        var planResourceId = new Azure.Core.ResourceIdentifier(config.AppServicePlanResourceId);
        var planResource = armClient.GetAppServicePlanResource(planResourceId);

        logger.LogInformation("Discovering sites in App Service Plan {PlanId}", config.AppServicePlanResourceId);

        var sites = new List<DiscoveredSite>();

        await foreach (var site in planResource.GetWebAppsAsync(cancellationToken: cancellationToken))
        {
            sites.Add(new DiscoveredSite(site.Name, site.Id.ToString()));
        }

        logger.LogInformation("Discovered {Count} sites in App Service Plan", sites.Count);

        _cachedSites = sites;
        _cacheExpiry = DateTimeOffset.UtcNow.AddMinutes(config.SiteDiscoveryIntervalMinutes);

        return _cachedSites;
    }
}
