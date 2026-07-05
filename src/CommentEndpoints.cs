namespace LiveCommentOverlay;

/// <summary>
/// The comment API under <c>/api/comments</c>: read the current comment, show a new one, clear it.
/// Every response is <c>application/json; charset=utf-8</c> with <c>Cache-Control: no-store</c> so
/// clients always read the latest state.
/// </summary>
public static class CommentEndpoints
{
    private const long MaxBodyBytes = 64 * 1024;
    private const string JsonContentType = "application/json; charset=utf-8";

    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/comments");

        // Dynamic responses are never cached. Setting the header in a filter (before the result runs)
        // applies it to every endpoint in the group without repeating it in each handler.
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        group.MapGet("/current", (IOverlayHub hub) =>
            Results.Json(new { comment = hub.Current }, contentType: JsonContentType));

        group.MapPost("/show", async (HttpRequest request, IOverlayHub hub) =>
        {
            var body = await JsonBody.ReadAsync(request, MaxBodyBytes);
            var comment = CommentSanitizer.Sanitize(body);
            await hub.ShowAsync(comment);
            return Results.Json(new { ok = true, comment }, contentType: JsonContentType);
        });

        group.MapPost("/clear", async (IOverlayHub hub) =>
        {
            await hub.ClearAsync();
            return Results.Json(new { ok = true, comment = (Comment?)null }, contentType: JsonContentType);
        });

        return endpoints;
    }
}
