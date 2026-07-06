using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class IngestionInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TODO: EF Core DbContext + Npgsql, MassTransit + outbox, provider adapter
        // registrations, Polly policies. Signature takes IConfiguration already so
        // call sites won't need to change again next step.
        return services;
    }
}
