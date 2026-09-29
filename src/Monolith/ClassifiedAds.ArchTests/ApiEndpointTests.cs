using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.WebAPI.Authorization;
using ClassifiedAds.WebAPI;
using ClassifiedAds.WebAPI.Models.Files;
using ClassifiedAds.WebAPI.RateLimiterPolicies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ClassifiedAds.ArchTests;

public class ApiEndpointTests
{
    [Fact]
    public async Task AllApiEndpointsCanBeMaterializedAndRequireAuthorization()
    {
        await using var app = CreateApp();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

        Assert.Equal(42, endpoints.Length);
        Assert.Equal(endpoints.Length, endpoints.Select(endpoint =>
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName).Distinct().Count());
        Assert.All(endpoints, endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));

        var products = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName == "Products_Get");
        Assert.Equal(Permissions.GetProducts, products.Metadata.GetMetadata<IAuthorizeData>().Policy);
        Assert.Equal(RateLimiterPolicyNames.DefaultPolicy, products.Metadata.GetMetadata<EnableRateLimitingAttribute>().PolicyName);

        var auditLogs = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName == "AuditLogEntries_Get");
        Assert.Equal(RateLimiterPolicyNames.GetAuditLogsPolicy, auditLogs.Metadata.GetMetadata<EnableRateLimitingAttribute>().PolicyName);

        var upload = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName == "Files_Upload");
        Assert.False(upload.Metadata.GetMetadata<IAntiforgeryMetadata>().RequiresValidation);
    }

    [Fact]
    public async Task InvalidRouteIdReturnsBadRequestBeforeRunningHandler()
    {
        await using var app = CreateApp();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName == "Products_GetById");
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.RouteValues["id"] = "not-a-guid";

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task UploadValidationRejectsMissingFileAndOverlongName()
    {
        var filter = new ModelValidationFilter<UploadFileModel>();
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(),
            new UploadFileModel { Name = new string('a', 51) });

        var result = await filter.InvokeAsync(context, _ => throw new InvalidOperationException("Invalid input reached the handler."));

        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        var details = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Contains(nameof(UploadFileModel.FormFile), details.Errors.Keys);
        Assert.Contains(nameof(UploadFileModel.Name), details.Errors.Keys);
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.MapApplicationEndpoints();
        return app;
    }
}
