namespace Microsoft.Extensions.DependencyInjection;

public static class LocationCatalogApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddLocationCatalogApplication(this IServiceCollection services)
    {
        // TODO: Location query handlers, LocationActivated/Deactivated publishing.
        return services;
    }
}
