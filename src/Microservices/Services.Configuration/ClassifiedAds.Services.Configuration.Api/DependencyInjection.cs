using ClassifiedAds.Services.Configuration.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace ClassifiedAds.Services.Configuration.Api;

public static class DependencyInjection
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        ConfigurationEntriesEndpoints.Map(app);
    }
}
