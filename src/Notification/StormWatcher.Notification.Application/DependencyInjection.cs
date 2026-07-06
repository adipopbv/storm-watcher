namespace Microsoft.Extensions.DependencyInjection;

public static class NotificationApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        // TODO: AnomalyDetected consumer, Alert rendering/templating, ntfy client calls.
        return services;
    }
}
