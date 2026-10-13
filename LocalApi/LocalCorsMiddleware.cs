using SilentPrintAgent.Configuration;

namespace SilentPrintAgent.LocalApi;

/// <summary>Restricts browser origins while allowing same-machine clients.</summary>
public sealed class LocalCorsMiddleware(RequestDelegate next, PrintAgentOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            if (!options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            context.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-Agent-Token";
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await next(context);
    }
}
