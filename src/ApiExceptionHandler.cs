using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace LiveCommentOverlay;

/// <summary>
/// Central error contract: maps a thrown <see cref="HttpError"/> to its status code (any other
/// exception becomes 500) and writes the <c>{ "error": message }</c> JSON body, mirroring the Node
/// server. Registered via <c>AddExceptionHandler</c> + <c>UseExceptionHandler</c>.
/// </summary>
public sealed class ApiExceptionHandler(JsonSerializerOptions json) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception is HttpError httpError
            ? httpError.StatusCode
            : StatusCodes.Status500InternalServerError;
        var message = string.IsNullOrEmpty(exception.Message) ? "Internal server error." : exception.Message;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, json), cancellationToken);
        return true;
    }
}
