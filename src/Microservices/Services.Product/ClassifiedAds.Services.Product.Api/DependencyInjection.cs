using ClassifiedAds.Services.Product.Api.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace ClassifiedAds.Services.Product.Api;

public static class DependencyInjection
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        CreateProductRequestHandler.Map(app);
        DeleteProductRequest.Map(app);
        ExportProductsAsCsvRequest.Map(app);
        ExportProductsAsPdfRequest.Map(app);
        GetProductAuditLogsRequest.Map(app);
        GetProductRequest.Map(app);
        GetProductsRequest.Map(app);
        ImportCsvRequestHandler.Map(app);
        UpdateProductRequestHandler.Map(app);
    }
}
