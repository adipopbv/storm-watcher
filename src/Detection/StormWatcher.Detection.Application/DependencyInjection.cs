namespace Microsoft.Extensions.DependencyInjection;

public static class DetectionApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDetectionApplication(this IServiceCollection services)
    {
        // TODO: HazardRules, Baseline/Deviation handlers, MassTransit consumers.
        return services;
    }
}
