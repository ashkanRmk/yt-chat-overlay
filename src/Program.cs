using System.Text.Encodings.Web;
using System.Text.Json;
using LiveCommentOverlay;

var builder = WebApplication.CreateBuilder(args);

// Match the Node server's quiet console: suppress framework Information logs (Kestrel's
// "Now listening on...") and print the same two lines ourselves below.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// The 64 KiB body cap is enforced by hand (JsonBody) so it produces the exact JSON 413 body.
// Disable Kestrel's own limit so ours always wins rather than Kestrel rejecting first.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var parsedPort) && parsedPort > 0
    ? parsedPort
    : 3000;
builder.WebHost.UseUrls($"http://127.0.0.1:{port}"); // loopback only, exactly like Node

// UnsafeRelaxedJsonEscaping emits non-ASCII (Persian/emoji) and < > & raw, like Node's JSON.stringify.
// CamelCase makes the Comment record serialize as authorName/message/avatarUrl/manual. The hub and the
// exception handler serialize with this exact instance; minimal-API results (Results.Json) are given
// the same settings via ConfigureHttpJsonOptions so every JSON response matches byte-for-byte.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

builder.Services.AddSingleton(jsonOptions);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
    options.SerializerOptions.Encoder = jsonOptions.Encoder;
});

builder.Services.AddSingleton<IOverlayHub, OverlayHub>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options => options.AddPolicy("overlay", policy => policy
    .AllowAnyOrigin()
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders("content-type")));

var app = builder.Build();

// Locate the unchanged public/ and extension/ folders at the repo root.
var repoRoot = StaticContent.FindRepoRoot(app.Environment.ContentRootPath);
var publicDir = Path.GetFullPath(Path.Combine(repoRoot, "public"));
var extensionDir = Path.GetFullPath(Path.Combine(repoRoot, "extension"));
var assetsDir = Path.GetFullPath(Path.Combine(publicDir, "assets"));

app.UseExceptionHandler();               // thrown HttpError/exception -> { error } JSON with its status
app.UseStatusCodePages(StatusPages.WriteJsonError); // empty 404/405 -> { error } JSON
app.UseMiddleware<CrossOriginResourcePolicyMiddleware>();
app.UseCors("overlay");
app.UseWebSockets();
app.UseOverlayStaticFiles(assetsDir, extensionDir);

app.MapCommentEndpoints();
app.MapPageEndpoints(publicDir);
app.MapWebSocketEndpoint();

Console.WriteLine($"Control page: http://127.0.0.1:{port}/");
Console.WriteLine($"OBS overlay:  http://127.0.0.1:{port}/overlay");

await app.RunAsync();

// Exposed so the xUnit test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
