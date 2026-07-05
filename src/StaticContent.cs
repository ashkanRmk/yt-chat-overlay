using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace LiveCommentOverlay;

/// <summary>
/// Serves the unchanged <c>public/assets</c> and <c>extension/</c> folders via the standard static-file
/// middleware. Content types force <c>charset=utf-8</c> on text assets (the RTL/Persian overlay depends
/// on UTF-8) and every asset is sent with <c>Cache-Control: no-store</c> so OBS never caches a stale file.
/// </summary>
public static class StaticContent
{
    public static WebApplication UseOverlayStaticFiles(this WebApplication app, string assetsDir, string extensionDir)
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".css"] = "text/css; charset=utf-8";
        contentTypes.Mappings[".html"] = "text/html; charset=utf-8";
        contentTypes.Mappings[".js"] = "text/javascript; charset=utf-8";
        contentTypes.Mappings[".json"] = "application/json; charset=utf-8";
        contentTypes.Mappings[".svg"] = "image/svg+xml";
        contentTypes.Mappings[".woff2"] = "font/woff2";

        app.UseStaticFiles(BuildOptions(assetsDir, "/assets", contentTypes));
        app.UseStaticFiles(BuildOptions(extensionDir, "/extension", contentTypes));
        return app;
    }

    private static StaticFileOptions BuildOptions(string rootDir, string requestPath, IContentTypeProvider contentTypes) =>
        new()
        {
            FileProvider = new PhysicalFileProvider(rootDir),
            RequestPath = requestPath,
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
        };

    /// <summary>
    /// Walks up from the content root to the repo root — the directory containing both <c>public/</c>
    /// and <c>extension/</c> — mirroring Node's <c>path.resolve(__dirname, "..")</c>. This keeps working
    /// regardless of where the project lives (e.g. under <c>src/</c>) as long as those folders sit at the root.
    /// </summary>
    public static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "public")) &&
                Directory.Exists(Path.Combine(dir.FullName, "extension")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetParent(start)?.FullName ?? start;
    }
}
