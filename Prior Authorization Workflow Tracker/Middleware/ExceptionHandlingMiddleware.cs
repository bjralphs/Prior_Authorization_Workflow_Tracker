using Prior_Authorization_Workflow_Tracker.Exceptions;

namespace Prior_Authorization_Workflow_Tracker.Middleware;

/// <summary>
/// Catches unhandled exceptions that escape the service layer and:
/// - Logs <see cref="AuthorizationException"/> at Warning level (NFR-006)
/// - Logs all other unhandled exceptions at Error level
/// Does NOT suppress exceptions — allows ASP.NET Core error handling to continue.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AuthorizationException ex)
        {
            // NFR-006: unauthorized access attempts logged at Warning with user identity
            var userId = context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var userName = context.User.Identity?.Name ?? "anonymous";

            _logger.LogWarning(ex,
                "Unauthorized access attempt by user {UserId} ({UserName}) on {Path}",
                userId, userName, context.Request.Path);

            throw; // re-throw so ASP.NET Core error middleware handles the response
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            throw;
        }
    }
}
