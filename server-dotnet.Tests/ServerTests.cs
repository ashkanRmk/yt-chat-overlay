using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Overlay.Tests;

// HTTP + WebSocket behaviour, ported from test/server.test.js. Each test gets a fresh app (fresh
// in-memory comment state) exactly like the Node suite started a fresh server per test.
public class ServerTests
{
    [Fact]
    public async Task Show_stores_sanitized_comment_and_broadcasts_it()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var socket = await TestSupport.ConnectWebSocket(factory);

        var init = await TestSupport.ReceiveJson(socket);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Null(init["comment"]);

        var response = await client.PostAsync("/api/comments/show", TestSupport.JsonContent("""
            {"authorName":"  Sara <script>  ","message":"  سلام  test 😀  ","avatarUrl":"javascript:alert(1)"}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comment = (await TestSupport.ReadJson(response))!["comment"]!;
        Assert.Equal("Sara <script>", (string?)comment["authorName"]);
        Assert.Equal("سلام test 😀", (string?)comment["message"]);
        Assert.Equal("", (string?)comment["avatarUrl"]);
        Assert.False((bool)comment["manual"]!);

        var show = await TestSupport.ReceiveJson(socket);
        Assert.Equal("show", (string?)show["type"]);
        Assert.Equal("Sara <script>", (string?)show["comment"]!["authorName"]);

        var current = await TestSupport.ReadJson(await client.GetAsync("/api/comments/current"));
        Assert.Equal("Sara <script>", (string?)current!["comment"]!["authorName"]);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task Clear_nulls_state_and_broadcasts_clear()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        await client.PostAsync("/api/comments/show",
            TestSupport.JsonContent("""{"authorName":"A","message":"B","avatarUrl":""}"""));

        var socket = await TestSupport.ConnectWebSocket(factory);
        var init = await TestSupport.ReceiveJson(socket);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal("A", (string?)init["comment"]!["authorName"]);

        var response = await client.PostAsync("/api/comments/clear", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TestSupport.ReadJson(response);
        Assert.True((bool)body!["ok"]!);
        Assert.Null(body["comment"]);

        var clear = await TestSupport.ReceiveJson(socket);
        Assert.Equal("clear", (string?)clear["type"]);
        Assert.False(clear.AsObject().ContainsKey("comment")); // Node broadcasts {type:"clear"} with no comment key

        var current = await TestSupport.ReadJson(await client.GetAsync("/api/comments/current"));
        Assert.Null(current!["comment"]);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task Manual_message_is_accepted_over_http()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/comments/show", TestSupport.JsonContent("""
            {"message":"  پیام دستی  ","manual":true,"authorName":"should-be-dropped","avatarUrl":"https://example.com/a.png"}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comment = (await TestSupport.ReadJson(response))!["comment"]!;
        Assert.Equal("", (string?)comment["authorName"]);
        Assert.Equal("پیام دستی", (string?)comment["message"]);
        Assert.Equal("", (string?)comment["avatarUrl"]);
        Assert.True((bool)comment["manual"]!);
    }

    [Fact]
    public async Task Manual_message_without_text_returns_400()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/comments/show",
            TestSupport.JsonContent("""{"manual":true,"message":"   "}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Safe_absolute_avatar_url_is_preserved_over_http()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        const string url = "https://yt3.ggpht.com/profile=s88-c-k-c0x00ffffff-no-rj?token=abc";
        var payload = new JsonObject { ["authorName"] = "@کاربر", ["message"] = "سلام", ["avatarUrl"] = url };

        var response = await client.PostAsync("/api/comments/show",
            TestSupport.JsonContent(payload.ToJsonString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(url, (string?)(await TestSupport.ReadJson(response))!["comment"]!["avatarUrl"]);
    }

    [Fact]
    public async Task Cors_headers_are_present_on_every_response()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/comments/current");

        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("GET,POST,OPTIONS", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Equal("content-type", response.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Equal("cross-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
    }

    [Fact]
    public async Task Options_preflight_returns_204()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/comments/show"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found.", (string?)(await TestSupport.ReadJson(response))!["error"]);
    }

    [Fact]
    public async Task Post_to_get_only_route_returns_405()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/", TestSupport.JsonContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("Method not allowed.", (string?)(await TestSupport.ReadJson(response))!["error"]);
    }

    [Fact]
    public async Task Oversized_body_returns_413()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var payload = new JsonObject { ["message"] = new string('x', 70 * 1024) };
        var response = await client.PostAsync("/api/comments/show",
            TestSupport.JsonContent(payload.ToJsonString()));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("Request body is too large.", (string?)(await TestSupport.ReadJson(response))!["error"]);
    }

    [Fact]
    public async Task Invalid_json_returns_400()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/comments/show", TestSupport.JsonContent("not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request body must be valid JSON.", (string?)(await TestSupport.ReadJson(response))!["error"]);
    }

    [Fact]
    public async Task Json_responses_carry_no_store_cache_control()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/comments/current");

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
    }
}
