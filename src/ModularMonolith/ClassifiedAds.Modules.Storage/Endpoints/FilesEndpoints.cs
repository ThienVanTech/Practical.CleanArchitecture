using ClassifiedAds.Application;
using ClassifiedAds.Contracts.AuditLog.DTOs;
using ClassifiedAds.Infrastructure.Storages;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.Modules.Storage.Authorization;
using ClassifiedAds.Modules.Storage.ConfigurationOptions;
using ClassifiedAds.Modules.Storage.Entities;
using ClassifiedAds.Modules.Storage.Models;
using ClassifiedAds.Modules.Storage.Queries;
using CryptographyHelper;
using CryptographyHelper.SymmetricAlgorithms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassifiedAds.Modules.Storage.Endpoints;

public class FilesEndpoints
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/files", Get)
            .WithTags("Files")
            .WithName("Files_Get")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetFiles)
            .Produces<IEnumerable<FileEntryModel>>(StatusCodes.Status200OK);

        builder.MapPost("api/files", Upload)
            .WithTags("Files")
            .WithName("Files_Upload")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.UploadFile)
            .AddEndpointFilter<ModelValidationFilter<UploadFileModel>>()
            .ProducesValidationProblem()
            .DisableAntiforgery()
            .Produces<FileEntryModel>(StatusCodes.Status200OK);

        builder.MapGet("api/files/{id}", GetById)
            .WithTags("Files")
            .WithName("Files_GetById")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetFile)
            .Produces<IEnumerable<FileEntryModel>>(StatusCodes.Status200OK);

        builder.MapGet("api/files/{id}/download", Download)
            .WithTags("Files")
            .WithName("Files_Download")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DownloadFile)
            .Produces(StatusCodes.Status200OK);

        builder.MapPut("api/files/{id}", Put)
            .WithTags("Files")
            .WithName("Files_Put")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.UpdateFile)
            .AddEndpointFilter<ModelValidationFilter<FileEntryModel>>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapDelete("api/files/{id}", Delete)
            .WithTags("Files")
            .WithName("Files_Delete")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DeleteFile)
            .Produces(StatusCodes.Status200OK);

        builder.MapGet("api/files/{id}/auditlogs", GetAuditLogs)
            .WithTags("Files")
            .WithName("Files_GetAuditLogs")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetFileAuditLogs)
            .Produces<IEnumerable<AuditLogEntryDTO>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher)
    {
        var fileEntries = await dispatcher.DispatchAsync(new GetFileEntriesQuery());
        return Results.Ok(fileEntries.ToModels());
    }

    private static async Task<IResult> Upload([FromForm] UploadFileModel model,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<StorageModuleOptions> options,
        [FromServices] IFileStorageManager fileManager)
    {
        var fileEntry = new FileEntry
        {
            Name = model.Name,
            Description = model.Description,
            Size = model.FormFile.Length,
            UploadedTime = DateTime.Now,
            FileName = model.FormFile.FileName,
            Encrypted = model.Encrypted,
        };

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        fileEntry.FileLocation = DateTime.Now.ToString("yyyy/MM/dd/") + fileEntry.Id;

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        var fileEntryDTO = fileEntry.ToModel();

        if (model.Encrypted)
        {
            var key = SymmetricCrypto.GenerateKey(32);
            var iv = SymmetricCrypto.GenerateKey(16);
            using (var inputStream = model.FormFile.OpenReadStream())
            using (var encryptedStream = new MemoryStream(inputStream
                    .UseAES(key)
                    .WithCipher(CipherMode.CBC)
                    .WithIV(iv)
                    .WithPadding(PaddingMode.PKCS7)
                    .Encrypt()))
            {
                await fileManager.CreateAsync(fileEntryDTO, encryptedStream);
            }

            var masterEncryptionKey = options.Value.MasterEncryptionKey;
            var encryptedKey = key
                .UseAES(masterEncryptionKey.FromBase64String())
                .WithCipher(CipherMode.CBC)
                .WithIV(iv)
                .WithPadding(PaddingMode.PKCS7)
                .Encrypt();

            fileEntry.EncryptionKey = encryptedKey.ToBase64String();
            fileEntry.EncryptionIV = iv.ToBase64String();
        }
        else
        {
            using var stream = model.FormFile.OpenReadStream();
            await fileManager.CreateAsync(fileEntryDTO, stream);
        }

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        return Results.Ok(fileEntry.ToModel());
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IAuthorizationService authorizationService,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id });

        if (fileEntry == null || fileEntry.Deleted)
        {
            // return Results.NotFound();
            return Results.NoContent();
        }

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Read);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        return Results.Ok(fileEntry.ToModel());
    }

    private static async Task<IResult> Download(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<StorageModuleOptions> options,
        [FromServices] IFileStorageManager fileManager,
        [FromServices] IAuthorizationService authorizationService,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id });

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Read);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        var rawData = await fileManager.ReadAsync(fileEntry.ToModel());
        var content = rawData;

        if (fileEntry.Encrypted)
        {
            var masterEncryptionKey = options.Value.MasterEncryptionKey;
            var encryptionKey = fileEntry.EncryptionKey.FromBase64String()
                      .UseAES(masterEncryptionKey.FromBase64String())
                      .WithCipher(CipherMode.CBC)
                      .WithIV(fileEntry.EncryptionIV.FromBase64String())
                      .WithPadding(PaddingMode.PKCS7)
                      .Decrypt();

            content = fileEntry.FileLocation != "Fake.txt"
                ? rawData
                .UseAES(encryptionKey)
                .WithCipher(CipherMode.CBC)
                .WithIV(fileEntry.EncryptionIV.FromBase64String())
                .WithPadding(PaddingMode.PKCS7)
                .Decrypt()
                : rawData;
        }

        return Results.File(content, MediaTypeNames.Application.Octet, WebUtility.HtmlEncode(fileEntry.FileName));
    }

    private static async Task<IResult> Put(Guid id, [FromBody] FileEntryModel model,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IAuthorizationService authorizationService,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id, ThrowNotFoundIfNull = true });

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Update);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        fileEntry.Name = model.Name;
        fileEntry.Description = model.Description;

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        return Results.Ok(model);
    }

    private static async Task<IResult> Delete(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IAuthorizationService authorizationService,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id });

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Delete);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        fileEntry.Deleted = true;
        fileEntry.DeletedDate = DateTimeOffset.Now;

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        return Results.Ok();
    }

    private static async Task<IResult> GetAuditLogs(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var logs = await dispatcher.DispatchAsync(new GetAuditEntriesQuery { ObjectId = id.ToString() });

        List<dynamic> entries = new List<dynamic>();
        FileEntry previous = null;
        foreach (var log in logs.OrderBy(x => x.CreatedDateTime))
        {
            var data = JsonSerializer.Deserialize<FileEntry>(log.Log);
            var highLight = new
            {
                Name = previous != null && data.Name != previous.Name,
                Description = previous != null && data.Description != previous.Description,
                FileName = previous != null && data.FileName != previous.FileName,
                FileLocation = previous != null && data.FileLocation != previous.FileLocation,
            };

            var entry = new
            {
                log.Id,
                log.UserName,
                Action = log.Action.Replace("_FILEENTRY", string.Empty),
                log.CreatedDateTime,
                data,
                highLight,
            };
            entries.Add(entry);

            previous = data;
        }

        return Results.Ok(entries.OrderByDescending(x => x.CreatedDateTime));
    }
}
