using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace LiveCommentOverlay;

/// <summary>
/// Gives the framework's empty-bodied routing errors — a 404 for an unmatched route and a 405 for a
/// method mismatch on a known route — the same <c>{ "error": message }</c> JSON body the Node server
/// returned. Errors thrown by handlers already carry a body (and content type) from
/// <see cref="ApiExceptionHandler"/>, so this only fills genuinely empty error responses.
/// </summary>
public static class StatusPages
{
    public static async Task WriteJsonError(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        var message = response.StatusCode switch
        {
            StatusCodes.Status404NotFound => "Not found.",
            StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
            _ => null,
        };

        if (message is null)
        {
            return;
        }

        var json = context.HttpContext.RequestServices.GetRequiredService<JsonSerializerOptions>();
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        await response.WriteAsync(JsonSerializer.Serialize(new { error = message }, json));
    }
}
