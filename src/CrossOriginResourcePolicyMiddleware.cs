namespace LiveCommentOverlay;

/// <summary>
/// Emits <c>Cross-Origin-Resource-Policy: cross-origin</c> on every response. This is NOT a CORS
/// header (cross-origin fetches are governed by the CORS middleware); it lets the overlay and the
/// extension load resources cross-origin under CORP/COEP checks, matching the Node server.
/// </summary>
public sealed class CrossOriginResourcePolicyMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return next(context);
    }
}
