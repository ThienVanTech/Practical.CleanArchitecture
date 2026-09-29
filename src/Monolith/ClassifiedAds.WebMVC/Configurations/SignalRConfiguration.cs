using ClassifiedAds.WebMVC.ConfigurationOptions;
using ClassifiedAds.WebMVC.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassifiedAds.WebMVC.Configurations;

public static class SignalRConfiguration
{
    public static IServiceCollection AddClassifiedAdsSignalR(this IServiceCollection services, AppSettings appSettings)
    {
        var signalRBuilder = services.AddSignalR();

        if (appSettings.SignalR?.UseMessagePack == true)
        {
            signalRBuilder.AddMessagePackProtocol();
        }

        if (appSettings.SignalR?.Backplane?.Provider == "Redis")
        {
            signalRBuilder.AddStackExchangeRedis(appSettings.SignalR.Backplane.Redis.ConnectionString);
        }
        else if (appSettings.SignalR?.Backplane?.Provider == "Azure")
        {
            signalRBuilder.AddAzureSignalR(appSettings.SignalR.Backplane.Azure.ConnectionString);
        }

        return services;
    }

    public static void MapClassifiedAdsHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<AuthorizedHub>("/AuthorizedHub");
        endpoints.MapHub<HealthCheckHub>("/HealthCheckHub");
        endpoints.MapHub<SimulatedLongRunningTaskHub>("/SimulatedLongRunningTaskHub");
    }
}
