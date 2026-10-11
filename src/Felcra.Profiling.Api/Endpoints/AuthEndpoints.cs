using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Application.Auth;
using Felcra.Profiling.Contracts.Auth;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Felcra.Profiling.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        group.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        // Holding the refresh token is proof enough; the access token may already have expired.
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me);
        group.MapPost("/change-password", ChangePassword).Validate<ChangePasswordRequest>();

        return app;
    }

    private static async Task<Results<Ok<TokenResponse>, ValidationProblem, UnauthorizedHttpResult>> Login(
        LoginRequest request, IValidator<LoginRequest> validator, IAuthService auth, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return TypedResults.ValidationProblem(validation.ToDictionary());

        var tokens = await auth.LoginAsync(request, ct);
        return tokens is null ? TypedResults.Unauthorized() : TypedResults.Ok(tokens);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> Refresh(
        RefreshRequest request, IAuthService auth, CancellationToken ct)
    {
        var tokens = await auth.RefreshAsync(request.RefreshToken, ct);
        return tokens is null ? TypedResults.Unauthorized() : TypedResults.Ok(tokens);
    }

    private static async Task<NoContent> Logout(RefreshRequest request, IAuthService auth, CancellationToken ct)
    {
        await auth.LogoutAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TokenResponse>, ValidationProblem>> ChangePassword(
        ChangePasswordRequest request, ICurrentUser currentUser, IAuthService auth, CancellationToken ct)
    {
        var tokens = await auth.ChangePasswordAsync(currentUser.UserId!.Value, request, ct);
        return tokens is not null
            ? TypedResults.Ok(tokens)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.CurrentPassword)] = ["The current password is not correct."] });
    }

    private static async Task<Results<Ok<CurrentUserDto>, NotFound>> Me(
        ICurrentUser currentUser, IAuthService auth, CancellationToken ct)
    {
        var user = currentUser.UserId is { } id ? await auth.GetUserAsync(id, ct) : null;
        return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
    }
}
