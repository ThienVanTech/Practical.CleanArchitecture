using ClassifiedAds.Services.Identity.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace ClassifiedAds.Services.Identity.Api;

public static class DependencyInjection
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        RolesEndpoints.Map(app);
        UsersEndpoints.Map(app);
    }
}
