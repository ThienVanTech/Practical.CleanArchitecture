using ClassifiedAds.Application;
using ClassifiedAds.Application.Roles.Commands;
using ClassifiedAds.Application.Roles.Queries;
using ClassifiedAds.Domain.Entities;
using ClassifiedAds.Infrastructure.Web.MinimalApis;
using ClassifiedAds.WebAPI.Authorization;
using ClassifiedAds.WebAPI.Models.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClassifiedAds.WebAPI.Endpoints;

public class RolesEndpoints
{
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("api/roles", Get)
            .WithTags("Roles")
            .WithName("Roles_Get")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetRoles)
            .Produces<IEnumerable<Role>>(StatusCodes.Status200OK);

        builder.MapGet("api/roles/{id}", GetById)
            .WithTags("Roles")
            .WithName("Roles_GetById")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.GetRole)
            .Produces<Role>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPost("api/roles", Post)
            .WithTags("Roles")
            .WithName("Roles_Post")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.AddRole)
            .AddEndpointFilter<ModelValidationFilter<RoleModel>>()
            .ProducesValidationProblem()
            .Produces<Role>(StatusCodes.Status201Created);

        builder.MapPut("api/roles/{id}", Put)
            .WithTags("Roles")
            .WithName("Roles_Put")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.UpdateRole)
            .AddEndpointFilter<ModelValidationFilter<RoleModel>>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        builder.MapDelete("api/roles/{id}", Delete)
            .WithTags("Roles")
            .WithName("Roles_Delete")
            .RequireAuthorization()
            .RequireAuthorization(Permissions.DeleteRole)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Get([FromServices] Dispatcher dispatcher)
    {
        var roles = await dispatcher.DispatchAsync(new GetRolesQuery { AsNoTracking = true });
        var model = roles.ToModels();
        return Results.Ok(model);
    }

    private static async Task<IResult> GetById(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var role = await dispatcher.DispatchAsync(new GetRoleQuery { Id = id, AsNoTracking = true });
        var model = role.ToModel();
        return Results.Ok(model);
    }

    private static async Task<IResult> Post([FromBody] RoleModel model,
        [FromServices] Dispatcher dispatcher)
    {
        var role = new Role
        {
            Name = model.Name,
            NormalizedName = model.Name.ToUpper(),
        };

        await dispatcher.DispatchAsync(new AddUpdateRoleCommand { Role = role });

        model = role.ToModel();

        return Results.Created($"/api/roles/{model.Id}", model);
    }

    private static async Task<IResult> Put(Guid id, [FromBody] RoleModel model,
        [FromServices] Dispatcher dispatcher)
    {
        var role = await dispatcher.DispatchAsync(new GetRoleQuery { Id = id });

        role.Name = model.Name;
        role.NormalizedName = model.Name.ToUpper();

        await dispatcher.DispatchAsync(new AddUpdateRoleCommand { Role = role });

        model = role.ToModel();

        return Results.Ok(model);
    }

    private static async Task<IResult> Delete(Guid id,
        [FromServices] Dispatcher dispatcher)
    {
        var role = await dispatcher.DispatchAsync(new GetRoleQuery { Id = id });
        await dispatcher.DispatchAsync(new DeleteRoleCommand { Role = role });

        return Results.Ok();
    }
}
