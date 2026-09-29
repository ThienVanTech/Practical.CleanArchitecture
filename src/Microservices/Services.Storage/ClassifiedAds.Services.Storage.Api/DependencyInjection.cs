using ClassifiedAds.Services.Storage.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace ClassifiedAds.Services.Storage.Api;

public static class DependencyInjection
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        FilesEndpoints.Map(app);
    }
}
