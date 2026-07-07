using StormWatcher.Ingestion.Scheduling;

namespace Microsoft.Extensions.DependencyInjection;

public static class IngestionApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionApplication(this IServiceCollection services)
    {
        // TODO: register application handlers (MassTransit consumers, use-case services)
        // as Ingestion business logic lands. Intentionally empty for now.
        services.AddSingleton<PollDispatchService>();
        return services;
    }
}
