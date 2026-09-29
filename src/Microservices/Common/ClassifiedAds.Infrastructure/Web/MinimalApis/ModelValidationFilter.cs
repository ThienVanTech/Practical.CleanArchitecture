using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ClassifiedAds.Infrastructure.Web.MinimalApis;

public class ModelValidationFilter<TModel> : IEndpointFilter
    where TModel : class
{
    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<TModel>().FirstOrDefault();
        if (model != null)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true))
            {
                var errors = results
                    .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty),
                        (result, member) => new { Member = member, result.ErrorMessage })
                    .GroupBy(error => error.Member)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }
        }

        return await next(context);
    }
}
