using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class LocationCatalogInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddLocationCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TODO: EF Core DbContext + Npgsql, MassTransit + outbox.
        return services;
    }
}
