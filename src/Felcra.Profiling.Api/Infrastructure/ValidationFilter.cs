using FluentValidation;

namespace Felcra.Profiling.Api.Infrastructure;

/// <summary>Validates the endpoint's <typeparamref name="T"/> argument with its FluentValidation validator.</summary>
internal sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is not { } argument) return await next(context);

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : TypedResults.ValidationProblem(result.ToDictionary());
    }
}

internal static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
