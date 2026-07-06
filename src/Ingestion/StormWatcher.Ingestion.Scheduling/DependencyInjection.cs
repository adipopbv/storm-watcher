using StormWatcher.Ingestion.Scheduling;

namespace Microsoft.Extensions.DependencyInjection;

public static class IngestionSchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionScheduling(this IServiceCollection services)
    {
        services.AddSingleton<PollDispatchService>();
        return services;
    }
}
