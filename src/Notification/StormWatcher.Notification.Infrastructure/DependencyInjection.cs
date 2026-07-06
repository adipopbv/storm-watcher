using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class NotificationInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TODO: EF Core DbContext + Npgsql, MassTransit + outbox, ntfy HttpClient.
        return services;
    }
}
