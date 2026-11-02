using System.Text.RegularExpressions;
using Serilog.Context;

namespace Csms.Host.Diagnostics;

/// <summary>
/// Gives every HTTP request a correlation ID. A valid incoming <c>X-Correlation-ID</c> is kept,
/// so a client can follow its own request through our logs; anything else is replaced.
/// The ID is echoed in the response header and attached to every log event of the request.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogProperty = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Parse(context.Request.Headers[HeaderName]) ?? Guid.NewGuid().ToString("N");

        // OnStarting, because headers can no longer change once the body has started.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Disposing the scope removes the property, so it never leaks into the next request.
        using (LogContext.PushProperty(LogProperty, correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Boundary check: the header comes from the client and ends up in logs and response headers.
    /// A short, plain-ASCII whitelist rules out log forging (newlines), markup and huge values.
    /// </summary>
    private static string? Parse(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Count == 1 && values[0] is { } value && ValidId().IsMatch(value) ? value : null;

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex ValidId();
}
