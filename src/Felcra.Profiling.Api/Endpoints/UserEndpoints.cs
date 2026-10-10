using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Application.Users;
using Felcra.Profiling.Contracts.Users;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Felcra.Profiling.Api.Endpoints;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Policies.HqAdmin);

        group.MapGet("/", async (IUserAdminService users, CancellationToken ct) => TypedResults.Ok(await users.ListAsync(ct)));

        group.MapGet("/{id:guid}", async Task<Results<Ok<UserDto>, NotFound>> (Guid id, IUserAdminService users, CancellationToken ct) =>
            await users.GetAsync(id, ct) is { } user ? TypedResults.Ok(user) : TypedResults.NotFound());

        group.MapPost("/", async Task<Results<Created<UserDto>, ValidationProblem>> (
                CreateUserRequest request, IUserAdminService users, CancellationToken ct) =>
            {
                var result = await users.CreateAsync(request, ct);
                return result.Succeeded
                    ? TypedResults.Created($"/api/users/{result.User!.Id}", result.User)
                    : TypedResults.ValidationProblem(result.Errors);
            })
            .Validate<CreateUserRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, IUserAdminService users, ICurrentUser currentUser, CancellationToken ct) =>
                ToResult(await users.UpdateAsync(id, request, currentUser.UserId!.Value, ct)))
            .Validate<UpdateUserRequest>();

        group.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, IUserAdminService users, CancellationToken ct) =>
                ToResult(await users.ResetPasswordAsync(id, request, ct)))
            .Validate<ResetPasswordRequest>();

        return app;
    }

    private static Results<Ok<UserDto>, NotFound, ValidationProblem> ToResult(UserCommandResult result) =>
        result switch
        {
            { NotFound: true } => TypedResults.NotFound(),
            { Succeeded: true } => TypedResults.Ok(result.User!),
            _ => TypedResults.ValidationProblem(result.Errors),
        };
}
