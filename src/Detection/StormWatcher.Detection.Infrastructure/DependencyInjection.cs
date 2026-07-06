using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class DetectionInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDetectionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TODO: EF Core DbContext + Npgsql, MassTransit + outbox.
        return services;
    }
}
