using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Overlay.Tests;

// Static file serving, ported from test/static-routes.test.js. (The overlay.js source-inspection
// tests in that file are language-agnostic and stay covered by the Node suite.)
public class StaticRoutesTests
{
    [Fact]
    public async Task Serves_the_control_page()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("YouTube Live Comment Overlay", html);
        Assert.Contains("yt-live-chat-text-message-renderer", html);
        Assert.Contains(@"id=""fixture-items""", html);
        Assert.Contains(@"id=""manual-form""", html);
    }

    [Fact]
    public async Task Serves_the_overlay_page_with_rtl_markup()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/overlay");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("comment-card", html);
        Assert.Contains(@"<html lang=""en"" dir=""rtl"">", html);
        Assert.Matches(@"id=""comment-card""[^>]+dir=""rtl""", html);
        Assert.Matches(@"id=""author-name""[^>]+dir=""rtl""", html);
    }

    [Fact]
    public async Task Unknown_top_level_path_is_404()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/fixture")).StatusCode);
    }

    [Fact]
    public async Task Serves_extension_files()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/extension/content.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("lco-show-button", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Serves_assets_with_expected_content()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var appCss = await client.GetAsync("/assets/app.css");
        Assert.Equal(HttpStatusCode.OK, appCss.StatusCode);
        Assert.Contains("app-shell", await appCss.Content.ReadAsStringAsync());

        var overlayJs = await client.GetAsync("/assets/overlay.js");
        Assert.Equal(HttpStatusCode.OK, overlayJs.StatusCode);
        Assert.Contains("initOverlay", await overlayJs.Content.ReadAsStringAsync());

        var overlayCss = await client.GetAsync("/assets/overlay.css");
        Assert.Equal(HttpStatusCode.OK, overlayCss.StatusCode);
        var overlayCssText = await overlayCss.Content.ReadAsStringAsync();
        Assert.Matches(@"font-family:\s*""Vazir""", overlayCssText);
        Assert.Matches(@"direction:\s*rtl", overlayCssText);
    }

    [Fact]
    public async Task Serves_the_bundled_font_with_woff2_content_type()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/fonts/Vazirmatn.woff2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("font/woff2", response.Content.Headers.ContentType!.ToString());
    }
}
