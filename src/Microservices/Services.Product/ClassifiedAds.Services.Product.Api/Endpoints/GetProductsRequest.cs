using ClassifiedAds.Application;
using ClassifiedAds.Services.Product.Authorization;
using ClassifiedAds.Services.Product.Models;
using ClassifiedAds.Services.Product.Queries;
using ClassifiedAds.Services.Product.RateLimiterPolicies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClassifiedAds.Services.Product.Api.Endpoints;

public class GetProductsRequest
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/products", HandleAsync)
        .RequireAuthorization(Permissions.GetProducts)
        .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
        .WithName("GetProducts")
        .Produces<IEnumerable<ProductModel>>(contentType: "application/json")
        .WithTags("Products");
    }

    private static async Task<IResult> HandleAsync(Dispatcher dispatcher,
        ILogger<GetProductsRequest> logger)
    {
        logger.LogInformation("Getting all products");
        var products = await dispatcher.DispatchAsync(new GetProductsQuery());
        var model = products.ToModels();
        return Results.Ok(model);
    }
}
