namespace LiveCommentOverlay;

/// <summary>
/// The two HTML pages served from <c>public/</c>: the operator control page (<c>/</c>) and the OBS
/// overlay (<c>/overlay</c>). Served with <c>text/html; charset=utf-8</c> (the overlay is RTL/Persian)
/// and <c>Cache-Control: no-store</c>.
/// </summary>
public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder endpoints, string publicDir)
    {
        endpoints.MapGet("/", (HttpResponse response) =>
            ServeHtml(response, Path.Combine(publicDir, "index.html")));

        endpoints.MapGet("/overlay", (HttpResponse response) =>
            ServeHtml(response, Path.Combine(publicDir, "overlay.html")));

        return endpoints;
    }

    private static IResult ServeHtml(HttpResponse response, string path)
    {
        response.Headers.CacheControl = "no-store";
        return Results.File(path, "text/html; charset=utf-8");
    }
}
