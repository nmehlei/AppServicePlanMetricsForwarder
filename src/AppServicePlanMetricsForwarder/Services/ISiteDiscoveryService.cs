namespace AppServicePlanMetricsForwarder.Services;

public record DiscoveredSite(string Name, string ResourceId);

public interface ISiteDiscoveryService
{
    Task<IReadOnlyList<DiscoveredSite>> GetSitesAsync(CancellationToken cancellationToken = default);
}
