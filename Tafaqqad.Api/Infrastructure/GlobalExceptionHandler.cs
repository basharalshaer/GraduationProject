using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tafaqqad.Api.Infrastructure;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        // The client closed the connection (e.g. left the page) - not a server error
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request cancelled by the client {TraceId} {Method} {Path}",
                traceId, context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, message) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode,
                bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The request is too large."
                    : "The request is invalid."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "This record was changed by someone else. Reload and try again."),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (StatusCodes.Status409Conflict,
                "This conflicts with an existing record."),
            _ => (StatusCodes.Status500InternalServerError,
                "Something went wrong on our side. Please try again.")
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception {TraceId} {Method} {Path}",
                traceId, context.Request.Method, context.Request.Path);
        else
            _logger.LogWarning(exception, "Request failed with {Status} {TraceId} {Method} {Path}",
                status, traceId, context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new
        {
            message,
            traceId,
            detail = _env.IsDevelopment() ? exception.ToString() : null   // never shown in Production
        }, ct);

        return true;
    }
}