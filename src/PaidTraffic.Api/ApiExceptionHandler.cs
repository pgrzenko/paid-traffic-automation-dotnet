using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PaidTraffic.Api.Operations;
using PaidTraffic.Domain;

namespace PaidTraffic.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            WorkflowBusyException => (409, "Another workflow is running; retry later."),
            InvalidTransitionException => (409, exception.Message),
            ArgumentException => (400, exception.Message),
            _ => (500, "An unexpected error occurred.")
        };
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}
