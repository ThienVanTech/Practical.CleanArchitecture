using ClassifiedAds.Application;
using ClassifiedAds.Application.AuditLogEntries.DTOs;
using ClassifiedAds.Application.AuditLogEntries.Queries;
using ClassifiedAds.Application.FileEntries.Queries;
using ClassifiedAds.Domain.Entities;
using ClassifiedAds.Domain.Infrastructure.Storages;
using ClassifiedAds.Domain.Repositories;
using ClassifiedAds.Infrastructure.AI;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.WebAPI.Authorization;
using ClassifiedAds.WebAPI.ConfigurationOptions;
using ClassifiedAds.WebAPI.Hubs;
using ClassifiedAds.WebAPI.Models.Files;
using CryptographyHelper;
using CryptographyHelper.SymmetricAlgorithms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassifiedAds.WebAPI.Endpoints;

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

        builder.MapGet("api/files/vectorsearch", VectorSearch)
            .WithTags("Files")
            .WithName("Files_VectorSearch")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetFiles)
            .Produces<IEnumerable<FileEntryVectorSearchResultModel>>(StatusCodes.Status200OK);

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

        builder.MapGet("api/files/{id}/downloadtext", DownloadText)
            .WithTags("Files")
            .WithName("Files_DownloadText")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DownloadFile)
            .Produces(StatusCodes.Status200OK);

        builder.MapGet("api/files/{id}/downloadchunk/{chunkName}", DownloadChunk)
            .WithTags("Files")
            .WithName("Files_DownloadChunk")
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

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher,
        [FromServices] IHubContext<NotificationHub> notificationHubContext,
        [FromServices] IStringLocalizer stringLocalizer)
    {
        await notificationHubContext.Clients.All.SendAsync("ReceiveMessage", $"{stringLocalizer["Getting files ..."]}");
        var fileEntries = await dispatcher.DispatchAsync(new GetFileEntriesQuery());
        return Results.Ok(fileEntries.ToModels());
    }

    private static async Task<IResult> VectorSearch(string searchText,
        [FromServices] IOptions<AppSettings> options,
        [FromServices] IFileStorageManager fileManager,
        [FromServices] IRepository<FileEntryEmbedding, Guid> fileEntryEmbeddingRepository,
        [FromServices] EmbeddingService embeddingService)
    {
        var embeddingRs = await embeddingService.GenerateAsync(searchText);
        var embedding = new SqlVector<float>(embeddingRs.Vector);

        var chunks = fileEntryEmbeddingRepository.GetQueryableSet()
                .Where(x => !x.FileEntry.Deleted)
                .OrderBy(x => EF.Functions.VectorDistance("cosine", x.Embedding, embedding))
                .Take(5)
                .Select(x => new
                {
                    x.FileEntry,
                    x.ChunkName,
                    x.ChunkLocation,
                    x.ShortText,
                    SimilarityScore = EF.Functions.VectorDistance("cosine", x.Embedding, embedding)
                }).ToList();

        var results = new List<FileEntryVectorSearchResultModel>();

        foreach (var chunk in chunks)
        {
            var result = new FileEntryVectorSearchResultModel
            {
                FileEntryId = chunk.FileEntry.Id,
                FileEntryName = chunk.FileEntry.Name,
                FileName = chunk.FileEntry.FileName,
                ChunkName = chunk.ChunkName,
                SimilarityScore = chunk.SimilarityScore
            };

            var fileExtension = Path.GetExtension(chunk.FileEntry.FileName);

            if (fileExtension == ".jpg" || fileExtension == ".png")
            {
                var content = await GetBytesAsync(chunk.FileEntry, options, fileManager);
                result.ChunkData = $"data:image/{fileExtension.TrimStart('.')};base64,{Convert.ToBase64String(content)}";
            }
            else
            {
                result.ChunkData = chunk.ShortText;
            }

            result.FileExtension = fileExtension;

            results.Add(result);
        }

        return Results.Ok(results);
    }

    private static async Task<IResult> Upload([FromForm] UploadFileModel model,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<AppSettings> options,
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
                await fileManager.CreateAsync(fileEntry, encryptedStream);
            }

            var masterEncryptionKey = options.Value.Storage.MasterEncryptionKey;
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
            await fileManager.CreateAsync(fileEntry, stream);
        }

        await dispatcher.DispatchAsync(new AddOrUpdateEntityCommand<FileEntry>(fileEntry));

        return Results.Ok(fileEntry.ToModel());
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IRepository<FileEntryText, Guid> fileEntryTextRepository,
        [FromServices] IRepository<FileEntryEmbedding, Guid> fileEntryEmbeddingRepository,
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

        var model = fileEntry.ToModel();

        model.FileEntryText = fileEntryTextRepository.GetQueryableSet()
            .Where(x => x.FileEntryId == fileEntry.Id)
            .Select(x => new FileEntryTextModel
            {
                TextLocation = x.TextLocation
            }).FirstOrDefault();

        model.FileEntryEmbeddings = fileEntryEmbeddingRepository.GetQueryableSet()
            .Where(x => x.FileEntryId == fileEntry.Id)
            .OrderBy(x => x.CreatedDateTime)
            .Select(x => new
            {
                x.ChunkName,
                x.ChunkLocation,
                x.ShortText,
                x.Embedding,
                x.TokenDetails,
                x.CreatedDateTime,
                x.UpdatedDateTime,
            })
            .AsEnumerable()
            .Select(x => new FileEntryEmbeddingModel
            {
                ChunkName = x.ChunkName,
                ChunkLocation = x.ChunkLocation,
                ShortText = x.ShortText,
                Embedding = JsonSerializer.Serialize(x.Embedding.Memory),
                TokenDetails = x.TokenDetails,
                CreatedDateTime = x.CreatedDateTime,
                UpdatedDateTime = x.UpdatedDateTime,
            })
            .ToList();

        return Results.Ok(model);
    }

    private static async Task<IResult> Download(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<AppSettings> options,
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

        var content = await GetBytesAsync(fileEntry, options, fileManager);

        return Results.File(content, MediaTypeNames.Application.Octet, WebUtility.HtmlEncode(fileEntry.FileName));
    }

    private static async Task<byte[]> GetBytesAsync(FileEntry fileEntry,
        IOptions<AppSettings> options,
        IFileStorageManager fileManager,
        CancellationToken cancellationToken = default)
    {
        var content = await fileManager.ReadAsync(fileEntry, cancellationToken);

        if (fileEntry.Encrypted)
        {
            var masterEncryptionKey = options.Value.Storage.MasterEncryptionKey;
            var encryptionKey = fileEntry.EncryptionKey.FromBase64String()
                      .UseAES(masterEncryptionKey.FromBase64String())
                      .WithCipher(CipherMode.CBC)
                      .WithIV(fileEntry.EncryptionIV.FromBase64String())
                      .WithPadding(PaddingMode.PKCS7)
                      .Decrypt();

            content = fileEntry.FileLocation != "Fake.txt"
                ? content
                .UseAES(encryptionKey)
                .WithCipher(CipherMode.CBC)
                .WithIV(fileEntry.EncryptionIV.FromBase64String())
                .WithPadding(PaddingMode.PKCS7)
                .Decrypt()
                : content;
        }

        return content;
    }

    private static async Task<IResult> DownloadText(Guid id,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<AppSettings> options,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IRepository<FileEntryText, Guid> fileEntryTextRepository,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id });

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Read);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        var fileEntryText = fileEntryTextRepository.GetQueryableSet()
              .Where(x => x.FileEntryId == fileEntry.Id)
              .Select(x => new FileEntryTextModel
              {
                  TextLocation = x.TextLocation
              }).FirstOrDefault();

        var stream = System.IO.File.OpenRead(Path.Combine(options.Value.Storage.TempFolderPath, fileEntryText.TextLocation));
        var ext = Path.GetExtension(fileEntryText.TextLocation).ToLowerInvariant();
        return Results.File(stream, MediaTypeNames.Application.Octet, WebUtility.HtmlEncode(fileEntry.FileName + ext));
    }

    private static async Task<IResult> DownloadChunk(Guid id, string chunkName,
        [FromServices] Dispatcher dispatcher,
        [FromServices] IOptions<AppSettings> options,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IRepository<FileEntryEmbedding, Guid> fileEntryEmbeddingRepository,
        HttpContext httpContext)
    {
        var fileEntry = await dispatcher.DispatchAsync(new GetEntityByIdQuery<FileEntry> { Id = id });

        var authorizationResult = await authorizationService.AuthorizeAsync(httpContext.User, fileEntry, Operations.Read);
        if (!authorizationResult.Succeeded)
        {
            return Results.Forbid();
        }

        var fileEntryEmbedding = fileEntryEmbeddingRepository.GetQueryableSet()
              .Where(x => x.FileEntryId == fileEntry.Id && x.ChunkName == chunkName)
              .Select(x => new FileEntryEmbeddingModel
              {
                  ChunkLocation = x.ChunkLocation,
              }).FirstOrDefault();

        var stream = System.IO.File.OpenRead(Path.Combine(options.Value.Storage.TempFolderPath, fileEntryEmbedding.ChunkLocation));
        return Results.File(stream, MediaTypeNames.Application.Octet, WebUtility.HtmlEncode(fileEntryEmbedding.ChunkName));
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
