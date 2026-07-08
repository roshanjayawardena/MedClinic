using Identity.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Identity.Features.RegisterUser;

internal static class RegisterUserEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", Handle)
            .WithName("RegisterUser")
            .WithTags("Identity")
            .WithSummary("Register a new clinic user (bootstrap only — secure with UsersManage permission in production)")
            .AddEndpointFilter<ValidationFilter<RegisterUserCommand>>()
            .RequireRateLimiting("auth")
            .AllowAnonymous()
            .Produces<RegisterUserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        RegisterUserCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/auth/users/{result.Value.UserId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
