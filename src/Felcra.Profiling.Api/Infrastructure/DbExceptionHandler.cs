using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Felcra.Profiling.Api.Infrastructure;

/// <summary>Turns expected database conflicts into 409 responses instead of 500s.</summary>
internal sealed class DbExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var detail = exception switch
        {
            DbUpdateConcurrencyException =>
                T("This record was changed by someone else. Reload it and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                T("A record with the same code or name already exists."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } =>
                T("This record is referenced by other data and cannot be changed this way."),
            _ => null,
        };
        if (detail is null) return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail },
        });
    }
}
