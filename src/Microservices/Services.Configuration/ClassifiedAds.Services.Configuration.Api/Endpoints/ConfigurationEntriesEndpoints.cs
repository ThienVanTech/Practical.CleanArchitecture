using ClassifiedAds.Application;
using ClassifiedAds.CrossCuttingConcerns.Excel;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.Services.Configuration.Authorization;
using ClassifiedAds.Services.Configuration.ConfigurationOptions;
using ClassifiedAds.Services.Configuration.Entities;
using ClassifiedAds.Services.Configuration.Excel;
using ClassifiedAds.Services.Configuration.Models;
using CryptographyHelper;
using CryptographyHelper.AsymmetricAlgorithms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;

namespace ClassifiedAds.Services.Configuration.Endpoints;

public class ConfigurationEntriesEndpoints
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/configurationentries", Get)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_Get")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetConfigurationEntries)
            .Produces<IEnumerable<ConfigurationEntryModel>>(StatusCodes.Status200OK);

        builder.MapGet("api/configurationentries/{id}", GetById)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_GetById")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetConfigurationEntry)
            .Produces<ConfigurationEntryModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPost("api/configurationentries", Post)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_Post")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.AddConfigurationEntry)
            .AddEndpointFilter<ModelValidationFilter<ConfigurationEntryModel>>()
            .ProducesValidationProblem()
            .Produces<ConfigurationEntryModel>(StatusCodes.Status201Created);

        builder.MapPut("api/configurationentries/{id}", Put)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_Put")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.UpdateConfigurationEntry)
            .AddEndpointFilter<ModelValidationFilter<ConfigurationEntryModel>>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapDelete("api/configurationentries/{id}", Delete)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_Delete")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DeleteConfigurationEntry)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapGet("api/configurationentries/ExportAsExcel", ExportAsExcel)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_ExportAsExcel")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK);

        builder.MapPost("api/configurationentries/ImportExcel", ImportExcel)
            .WithTags("ConfigurationEntries")
            .WithName("ConfigurationEntries_ImportExcel")
            .RequireAuthorization()
            .AddEndpointFilter<ModelValidationFilter<UploadFileModel>>()
            .ProducesValidationProblem()
            .DisableAntiforgery()
            .Produces(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher)
    {
        var entities = await dispatcher.DispatchAsync(new GetEntititesQuery<ConfigurationEntry>());
        var model = entities.OrderBy(x => x.Key).ToModels();
        return Results.Ok(model);
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var entity = await dispatcher.DispatchAsync(new GetEntityByIdQuery<ConfigurationEntry> { Id = id, ThrowNotFoundIfNull = true });
        var model = entity.ToModel();
        return Results.Ok(model);
    }

    private static async Task<IResult> Post([FromBody] ConfigurationEntryModel model,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptionsSnapshot<AppSettings> appSettings)
    {
        var entity = model.ToEntity();

        if (entity.IsSensitive)
        {
            var cert = appSettings.Value.Certificates.SettingsEncryption.FindCertificate();
            var encrypted = entity.Value.UseRSA(cert).Encrypt().ToBase64String();
            entity.Value = encrypted;
        }

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<ConfigurationEntry>(entity));
        model = entity.ToModel();
        return Results.Created($"/api/ConfigurationEntries/{model.Id}", model);
    }

    private static async Task<IResult> Put(Guid id, [FromBody] ConfigurationEntryModel model,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptionsSnapshot<AppSettings> appSettings)
    {
        var entity = await dispatcher.DispatchAsync(new GetEntityByIdQuery<ConfigurationEntry> { Id = id, ThrowNotFoundIfNull = true });

        entity.Key = model.Key;
        entity.Value = model.Value;
        entity.Description = model.Description;
        entity.IsSensitive = model.IsSensitive;

        if (entity.IsSensitive)
        {
            var cert = appSettings.Value.Certificates.SettingsEncryption.FindCertificate();
            var encrypted = entity.Value.UseRSA(cert).Encrypt().ToBase64String();
            entity.Value = encrypted;
        }

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<ConfigurationEntry>(entity));

        model = entity.ToModel();

        return Results.Ok(model);
    }

    private static async Task<IResult> Delete(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var entity = await dispatcher.DispatchAsync(new GetEntityByIdQuery<ConfigurationEntry> { Id = id, ThrowNotFoundIfNull = true });

        await dispatcher.DispatchAsync(new DeleteEntityCommand<ConfigurationEntry> { Entity = entity });

        return Results.Ok();
    }

    private static async Task<IResult> ExportAsExcel([FromServices] Dispatcher dispatcher,
        [FromServices] IExcelWriter<ExportConfigurationEntriesToExcel> configurationEntriesExcelWriter)
    {
        var entries = await dispatcher.DispatchAsync(new GetEntititesQuery<ConfigurationEntry>());
        using var stream = new MemoryStream();
        await configurationEntriesExcelWriter.WriteAsync(new ExportConfigurationEntriesToExcel { ConfigurationEntries = entries }, stream);
        return Results.File(stream.ToArray(), MediaTypeNames.Application.Octet, "ConfigurationEntries.xlsx");
    }

    private static async Task<IResult> ImportExcel([FromForm] UploadFileModel model,
        [FromServices] IExcelReader<ImportConfigurationEntriesFromExcel> configurationEntriesExcelReader)
    {
        using var stream = model.FormFile.OpenReadStream();
        var entries = await configurationEntriesExcelReader.ReadAsync(stream);

        // TODO: import to database
        return Results.Ok(entries.ConfigurationEntries);
    }
}
