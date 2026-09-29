using ClassifiedAds.Application;
using ClassifiedAds.Application.Common.DTOs;
using ClassifiedAds.Contracts.AuditLog.DTOs;
using ClassifiedAds.Modules.AuditLog.Authorization;
using ClassifiedAds.Modules.AuditLog.Queries;
using ClassifiedAds.Modules.AuditLog.RateLimiterPolicies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClassifiedAds.Modules.AuditLog.Endpoints;

public class AuditLogEntriesEndpoints
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/auditlogentries", Get)
            .WithTags("AuditLogEntries")
            .WithName("AuditLogEntries_Get")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetAuditLogs)
            .RequireRateLimiting(RateLimiterPolicyNames.GetAuditLogsPolicy)
            .Produces<IEnumerable<AuditLogEntryDTO>>(StatusCodes.Status200OK);

        builder.MapGet("api/auditlogentries/paged", GetPaged)
            .WithTags("AuditLogEntries")
            .WithName("AuditLogEntries_GetPaged")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetAuditLogs)
            .Produces<Paged<AuditLogEntryDTO>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher)
    {
        var logs = await dispatcher.DispatchAsync(new GetAuditEntriesQuery { });
        return Results.Ok(logs);
    }

    private static async Task<IResult> GetPaged([FromServices] Dispatcher dispatcher,
        int page = 0, int pageSize = 0)
    {
        var logs = await dispatcher.DispatchAsync(new GetPagedAuditEntriesQuery { Page = page, PageSize = pageSize });
        return Results.Ok(logs);
    }
}
