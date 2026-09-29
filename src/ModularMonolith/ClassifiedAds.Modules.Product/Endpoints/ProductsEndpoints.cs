using ClassifiedAds.Application;
using ClassifiedAds.Contracts.AuditLog.DTOs;
using ClassifiedAds.CrossCuttingConcerns.Csv;
using ClassifiedAds.CrossCuttingConcerns.Pdf;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.Modules.Product.Authorization;
using ClassifiedAds.Modules.Product.Commands;
using ClassifiedAds.Modules.Product.Csv;
using ClassifiedAds.Modules.Product.Models;
using ClassifiedAds.Modules.Product.Pdf;
using ClassifiedAds.Modules.Product.Queries;
using ClassifiedAds.Modules.Product.RateLimiterPolicies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassifiedAds.Modules.Product.Endpoints;

public class ProductsEndpoints
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/products", Get)
            .WithTags("Products")
            .WithName("Products_Get")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetProducts)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces<IEnumerable<Entities.Product>>(StatusCodes.Status200OK);

        builder.MapGet("api/products/{id}", GetById)
            .WithTags("Products")
            .WithName("Products_GetById")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces<Entities.Product>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPost("api/products", Post)
            .WithTags("Products")
            .WithName("Products_Post")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.AddProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .AddEndpointFilter<ModelValidationFilter<ProductModel>>()
            .ProducesValidationProblem()
            .Produces<Entities.Product>(StatusCodes.Status201Created);

        builder.MapPut("api/products/{id}", Put)
            .WithTags("Products")
            .WithName("Products_Put")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.UpdateProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .AddEndpointFilter<ModelValidationFilter<ProductModel>>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapDelete("api/products/{id}", Delete)
            .WithTags("Products")
            .WithName("Products_Delete")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DeleteProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapGet("api/products/{id}/auditlogs", GetAuditLogs)
            .WithTags("Products")
            .WithName("Products_GetAuditLogs")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetProductAuditLogs)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces<IEnumerable<AuditLogEntryDTO>>(StatusCodes.Status200OK);

        builder.MapGet("api/products/exportaspdf", ExportAsPdf)
            .WithTags("Products")
            .WithName("Products_ExportAsPdf")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces(StatusCodes.Status200OK);

        builder.MapGet("api/products/exportascsv", ExportAsCsv)
            .WithTags("Products")
            .WithName("Products_ExportAsCsv")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces(StatusCodes.Status200OK);

        builder.MapPost("api/products/importcsv", ImportCsv)
            .WithTags("Products")
            .WithName("Products_ImportCsv")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .AddEndpointFilter<ModelValidationFilter<UploadFileModel>>()
            .ProducesValidationProblem()
            .DisableAntiforgery()
            .Produces(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher,
        [FromServices] ILogger<ProductsEndpoints> logger)
    {
        logger.LogInformation("Getting all products");
        var products = await dispatcher.DispatchAsync(new GetProductsQuery());
        var model = products.ToModels();
        return Results.Ok(model);
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var product = await dispatcher.DispatchAsync(new GetProductQuery { Id = id, ThrowNotFoundIfNull = true });
        var model = product.ToModel();
        return Results.Ok(model);
    }

    private static async Task<IResult> Post([FromBody] ProductModel model,
        [FromServices] Dispatcher dispatcher)
    {
        var product = model.ToEntity();
        await dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });
        model = product.ToModel();
        return Results.Created($"/api/products/{model.Id}", model);
    }

    private static async Task<IResult> Put(Guid id, [FromBody] ProductModel model,
        [FromServices] Dispatcher dispatcher)
    {
        var product = await dispatcher.DispatchAsync(new GetProductQuery { Id = id, ThrowNotFoundIfNull = true });

        product.Code = model.Code;
        product.Name = model.Name;
        product.Description = model.Description;

        await dispatcher.DispatchAsync(new AddUpdateProductCommand { Product = product });

        model = product.ToModel();

        return Results.Ok(model);
    }

    private static async Task<IResult> Delete(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var product = await dispatcher.DispatchAsync(new GetProductQuery { Id = id, ThrowNotFoundIfNull = true });

        await dispatcher.DispatchAsync(new DeleteProductCommand { Product = product });

        return Results.Ok();
    }

    private static async Task<IResult> GetAuditLogs(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var logs = await dispatcher.DispatchAsync(new GetAuditEntriesQuery { ObjectId = id.ToString() });

        List<dynamic> entries = new List<dynamic>();
        ProductModel previous = null;
        foreach (var log in logs.OrderBy(x => x.CreatedDateTime))
        {
            var data = JsonSerializer.Deserialize<ProductModel>(log.Log);
            var highLight = new
            {
                Code = previous != null && data.Code != previous.Code,
                Name = previous != null && data.Name != previous.Name,
                Description = previous != null && data.Description != previous.Description,
            };

            var entry = new
            {
                log.Id,
                log.UserName,
                Action = log.Action.Replace("_PRODUCT", string.Empty),
                log.CreatedDateTime,
                data,
                highLight,
            };
            entries.Add(entry);

            previous = data;
        }

        return Results.Ok(entries.OrderByDescending(x => x.CreatedDateTime));
    }

    private static async Task<IResult> ExportAsPdf([FromServices] Dispatcher dispatcher,
        [FromServices] IPdfWriter<ExportProductsToPdf> pdfWriter)
    {
        var products = await dispatcher.DispatchAsync(new GetProductsQuery());
        var bytes = await pdfWriter.GetBytesAsync(new ExportProductsToPdf { Products = products });
        return Results.File(bytes, MediaTypeNames.Application.Octet, "Products.pdf");
    }

    private static async Task<IResult> ExportAsCsv([FromServices] Dispatcher dispatcher,
        [FromServices] ICsvWriter<ExportProductsToCsv> productCsvWriter)
    {
        var products = await dispatcher.DispatchAsync(new GetProductsQuery());
        using var stream = new MemoryStream();
        await productCsvWriter.WriteAsync(new ExportProductsToCsv { Products = products }, stream);
        return Results.File(stream.ToArray(), MediaTypeNames.Application.Octet, "Products.csv");
    }

    private static async Task<IResult> ImportCsv([FromForm] UploadFileModel model,
        [FromServices] ICsvReader<ImportProductsFromCsv> productCsvReader)
    {
        using var stream = model.FormFile.OpenReadStream();
        var result = await productCsvReader.ReadAsync(stream);

        // TODO: import to database
        return Results.Ok(result.Products);
    }
}
