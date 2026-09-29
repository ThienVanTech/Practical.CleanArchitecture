using ClassifiedAds.Services.AuditLog.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace ClassifiedAds.Services.AuditLog.Api;

public static class DependencyInjection
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        AuditLogEntriesEndpoints.Map(app);
    }
}
