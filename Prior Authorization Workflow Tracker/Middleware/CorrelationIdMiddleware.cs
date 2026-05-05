using Serilog.Context;

namespace Prior_Authorization_Workflow_Tracker.Middleware;

/// <summary>
/// Generates a per-request CorrelationId and enriches Serilog's LogContext with it
/// (BLIND-002 mitigation, §17.3).
///
/// The CorrelationId is:
///  - Read from the incoming "X-Correlation-Id" header if present (caller-provided)
///  - Otherwise generated as a new Guid
///
/// The resolved value is written back to the response header so callers can
/// correlate client-side errors with server logs.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N")[..16]; // 16-char hex prefix

        context.Response.Headers[HeaderName] = correlationId;
        // Make the ID available to services (e.g. AuditService) via HttpContext.Items (BUG-F fix)
        context.Items["CorrelationId"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
