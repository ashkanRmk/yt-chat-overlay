using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Overlay;

var builder = WebApplication.CreateBuilder(args);

// Match the Node server's quiet console: suppress framework Information logs (Kestrel's
// "Now listening on...") and print the same two lines ourselves below.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// The Node body cap (64 KiB) is enforced by hand below so it produces the exact JSON 413 body.
// Disable Kestrel's own limit so ours always wins rather than Kestrel rejecting first.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var parsedPort) && parsedPort > 0
    ? parsedPort
    : 3000;
builder.WebHost.UseUrls($"http://127.0.0.1:{port}"); // loopback only, exactly like Node

// Serve the existing public/ and extension/ folders unchanged. Walk up from the content root to
// the repo root (the directory that contains both), mirroring Node's path.resolve(__dirname, "..").
var repoRoot = FindRepoRoot(builder.Environment.ContentRootPath);
var publicDir = Path.GetFullPath(Path.Combine(repoRoot, "public"));
var extensionDir = Path.GetFullPath(Path.Combine(repoRoot, "extension"));
var assetsDir = Path.GetFullPath(Path.Combine(publicDir, "assets"));

// UnsafeRelaxedJsonEscaping emits non-ASCII (Persian/emoji) and < > & raw, like Node's
// JSON.stringify. CamelCase makes the Comment record serialize as authorName/message/avatarUrl/manual.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var hub = new OverlayHub(jsonOptions);

var app = builder.Build();

// addCorsHeaders (server.js:99-104) ran on every response, and OPTIONS short-circuited to 204.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Access-Control-Allow-Origin"] = "*";
    headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
    headers["Access-Control-Allow-Headers"] = "content-type";
    headers["Cross-Origin-Resource-Policy"] = "cross-origin";

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = 204;
        return;
    }

    await next();
});

app.UseWebSockets();

// One terminal handler mirroring the server.js request handler + upgrade handler line-for-line.
// Routing on Request.Path with explicit case-sensitive comparisons (rather than MapGet route
// templates) reproduces Node's exact path matching and the 405-before-static precedence.
app.Run(async context =>
{
    var request = context.Request;
    var method = request.Method;
    var pathname = request.Path.Value ?? "";

    // WebSocket upgrade (server.js:80-91). Only /ws is accepted; anything else is dropped.
    if (context.WebSockets.IsWebSocketRequest)
    {
        if (pathname != "/ws")
        {
            context.Abort();
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await hub.HandleClientAsync(socket, context.RequestAborted);
        return;
    }

    try
    {
        if (method == "GET" && pathname == "/api/comments/current")
        {
            await SendJson(context, 200, new { comment = hub.Current });
            return;
        }

        if (method == "POST" && pathname == "/api/comments/show")
        {
            var body = await ReadJsonBody(request);
            var comment = CommentSanitizer.Sanitize(body);
            await hub.ShowAsync(comment);
            await SendJson(context, 200, new { ok = true, comment });
            return;
        }

        if (method == "POST" && pathname == "/api/comments/clear")
        {
            await hub.ClearAsync();
            await SendJson(context, 200, new { ok = true, comment = (Comment?)null });
            return;
        }

        if (method != "GET")
        {
            await SendJson(context, 405, new { error = "Method not allowed." });
            return;
        }

        if (pathname == "/")
        {
            await ServeFile(context, Path.Combine(publicDir, "index.html"));
            return;
        }

        if (pathname == "/overlay")
        {
            await ServeFile(context, Path.Combine(publicDir, "overlay.html"));
            return;
        }

        if (pathname.StartsWith("/assets/", StringComparison.Ordinal))
        {
            await ServeFromRoot(context, assetsDir, pathname["/assets/".Length..]);
            return;
        }

        if (pathname.StartsWith("/extension/", StringComparison.Ordinal))
        {
            await ServeFromRoot(context, extensionDir, pathname["/extension/".Length..]);
            return;
        }

        await SendJson(context, 404, new { error = "Not found." });
    }
    catch (Exception error)
    {
        var status = error is HttpError httpError ? httpError.StatusCode : 500;
        var message = string.IsNullOrEmpty(error.Message) ? "Internal server error." : error.Message;
        await SendJson(context, status, new { error = message });
    }
});

Console.WriteLine($"Control page: http://127.0.0.1:{port}/");
Console.WriteLine($"OBS overlay:  http://127.0.0.1:{port}/overlay");

await app.RunAsync();

// ----- local helpers (mirror sendJson / readJsonBody / serveFromRoot / serveFile / contentTypeFor) -----

async Task SendJson(HttpContext context, int statusCode, object payload)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.Headers["Cache-Control"] = "no-store";
    await context.Response.WriteAsync(JsonSerializer.Serialize(payload, jsonOptions));
}

async Task<JsonNode?> ReadJsonBody(HttpRequest request)
{
    using var memory = new MemoryStream();
    var buffer = new byte[8192];
    long size = 0;
    int read;
    while ((read = await request.Body.ReadAsync(buffer)) > 0)
    {
        size += read;
        if (size > 64 * 1024)
        {
            throw new HttpError(413, "Request body is too large.");
        }

        memory.Write(buffer, 0, read);
    }

    var raw = Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    if (raw.Length == 0)
    {
        return new JsonObject(); // empty body -> {} (server.js:121-123)
    }

    try
    {
        return JsonNode.Parse(raw);
    }
    catch (JsonException)
    {
        throw new HttpError(400, "Request body must be valid JSON.");
    }
}

async Task ServeFromRoot(HttpContext context, string root, string relativePath)
{
    string fullPath;
    try
    {
        // Request.Path is already percent-decoded by ASP.NET, so no decode step is needed here
        // (Node called decodeURIComponent). Combine + GetFullPath then normalizes any . / .. segments.
        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
    }
    catch (ArgumentException)
    {
        await SendJson(context, 400, new { error = "Invalid path." });
        return;
    }

    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
        await SendJson(context, 403, new { error = "Forbidden." });
        return;
    }

    await ServeFile(context, fullPath);
}

async Task ServeFile(HttpContext context, string filePath)
{
    byte[] data;
    try
    {
        data = await File.ReadAllBytesAsync(filePath);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
    {
        await SendJson(context, 404, new { error = "Not found." });
        return;
    }
    catch
    {
        await SendJson(context, 500, new { error = "Unable to read file." });
        return;
    }

    context.Response.StatusCode = 200;
    context.Response.ContentType = ContentTypeFor(filePath);
    context.Response.Headers["Cache-Control"] = "no-store";
    await context.Response.Body.WriteAsync(data);
}

static string ContentTypeFor(string filePath) => Path.GetExtension(filePath) switch
{
    ".css" => "text/css; charset=utf-8",
    ".html" => "text/html; charset=utf-8",
    ".js" => "text/javascript; charset=utf-8",
    ".json" => "application/json; charset=utf-8",
    ".svg" => "image/svg+xml",
    ".woff2" => "font/woff2",
    _ => "application/octet-stream",
};

static string FindRepoRoot(string start)
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

// Exposed so the xUnit test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
