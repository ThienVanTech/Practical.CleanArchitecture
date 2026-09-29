using ClassifiedAds.Application;
using ClassifiedAds.Application.AuditLogEntries.DTOs;
using ClassifiedAds.Application.AuditLogEntries.Queries;
using ClassifiedAds.Application.Products.Commands;
using ClassifiedAds.Application.Products.DTOs;
using ClassifiedAds.Application.Products.Queries;
using ClassifiedAds.CrossCuttingConcerns.Csv;
using ClassifiedAds.CrossCuttingConcerns.Pdf;
using ClassifiedAds.Domain.Entities;
using ClassifiedAds.Domain.Repositories;
using ClassifiedAds.Infrastructure.AI;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.WebAPI.Authorization;
using ClassifiedAds.WebAPI.Models.Products;
using ClassifiedAds.WebAPI.RateLimiterPolicies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassifiedAds.WebAPI.Endpoints;

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
            .Produces<IEnumerable<Product>>(StatusCodes.Status200OK);

        builder.MapGet("api/products/vectorsearch", VectorSearch)
            .WithTags("Products")
            .WithName("Products_VectorSearch")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetProducts)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces<IEnumerable<ProductModel>>(StatusCodes.Status200OK);

        builder.MapGet("api/products/{id}", GetById)
            .WithTags("Products")
            .WithName("Products_GetById")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .Produces<ProductModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPost("api/products", Post)
            .WithTags("Products")
            .WithName("Products_Post")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.AddProduct)
            .RequireRateLimiting(RateLimiterPolicyNames.DefaultPolicy)
            .AddEndpointFilter<ModelValidationFilter<ProductModel>>()
            .ProducesValidationProblem()
            .Produces<Product>(StatusCodes.Status201Created);

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

    private static async Task<IResult> VectorSearch(string searchText,
        [FromServices] IRepository<ProductEmbedding, Guid> productEmbeddingRepository,
        [FromServices] EmbeddingService embeddingService)
    {
        var embeddingRs = await embeddingService.GenerateAsync(searchText);
        var embedding = new SqlVector<float>(embeddingRs.Vector);

        var products = productEmbeddingRepository.GetQueryableSet()
                .OrderBy(x => EF.Functions.VectorDistance("cosine", x.Embedding, embedding))
                .Take(5)
                .Select(x => new ProductModel
                {
                    Id = x.Product.Id,
                    Code = x.Product.Code,
                    Name = x.Product.Name,
                    Description = x.Product.Description,
                    SimilarityScore = EF.Functions.VectorDistance("cosine", x.Embedding, embedding)
                }).ToList();

        return Results.Ok(products);
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IRepository<ProductEmbedding, Guid> productEmbeddingRepository)
    {
        var product = await dispatcher.DispatchAsync(new GetProductQuery { Id = id, ThrowNotFoundIfNull = true });
        var model = product.ToModel();

        var embedding = productEmbeddingRepository.GetQueryableSet().Where(x => x.ProductId == id)
            .Select(x => new
            {
                x.Text,
                x.Embedding,
                x.TokenDetails,
                x.CreatedDateTime,
                x.UpdatedDateTime,
            })
            .FirstOrDefault();

        if (embedding != null)
        {
            model.ProductEmbedding = new ProductEmbeddingModel
            {
                Text = embedding.Text,
                Embedding = JsonSerializer.Serialize(embedding.Embedding.Memory),
                TokenDetails = embedding.TokenDetails,
                CreatedDateTime = embedding.CreatedDateTime,
                UpdatedDateTime = embedding.UpdatedDateTime,
            };

            var similarProducts = productEmbeddingRepository.GetQueryableSet()
                .Where(x => x.ProductId != id)
                .OrderBy(x => EF.Functions.VectorDistance("cosine", x.Embedding, embedding.Embedding))
                .Take(5)
                .Select(x => new SimilarProductModel
                {
                    Id = x.Product.Id,
                    Code = x.Product.Code,
                    Name = x.Product.Name,
                    Description = x.Product.Description,
                    SimilarityScore = EF.Functions.VectorDistance("cosine", x.Embedding, embedding.Embedding)
                }).ToList();

            model.SimilarProducts = similarProducts;
        }

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
        ProductDTO previous = null;
        foreach (var log in logs.OrderBy(x => x.CreatedDateTime))
        {
            var data = JsonSerializer.Deserialize<ProductDTO>(log.Log);
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
