using System.Text.Json.Nodes;

namespace LiveCommentOverlay.Tests;

// Direct unit tests for the sanitizer port. These mirror the behavioural expectations baked into
// test/server.test.js, plus a few extra edge cases (the U+0085 divergence guard, control-char
// stripping, non-object payloads).
public class SanitizerTests
{
    [Fact]
    public void Normalizes_text_without_escaping_html_and_rejects_javascript_avatar()
    {
        var input = JsonNode.Parse("""
            {"authorName":"  Sara <script>  ","message":"  سلام  test 😀  ","avatarUrl":"javascript:alert(1)"}
            """);

        var comment = CommentSanitizer.Sanitize(input);

        Assert.Equal("Sara <script>", comment.AuthorName);
        Assert.Equal("سلام test 😀", comment.Message);
        Assert.Equal("", comment.AvatarUrl);
        Assert.False(comment.Manual);
    }

    [Fact]
    public void Manual_message_drops_author_and_avatar()
    {
        var input = JsonNode.Parse("""
            {"message":"  پیام دستی  ","manual":true,"authorName":"should-be-dropped","avatarUrl":"https://example.com/a.png"}
            """);

        var comment = CommentSanitizer.Sanitize(input);

        Assert.Equal("", comment.AuthorName);
        Assert.Equal("پیام دستی", comment.Message);
        Assert.Equal("", comment.AvatarUrl);
        Assert.True(comment.Manual);
    }

    [Fact]
    public void Manual_message_without_text_is_rejected()
    {
        var input = JsonNode.Parse("""{"manual":true,"message":"   "}""");

        var error = Assert.Throws<HttpError>(() => CommentSanitizer.Sanitize(input));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("A message is required.", error.Message);
    }

    [Fact]
    public void Regular_comment_requires_author_and_message()
    {
        var error = Assert.Throws<HttpError>(() =>
            CommentSanitizer.Sanitize(JsonNode.Parse("""{"message":"hi"}""")));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("Both authorName and message are required.", error.Message);
    }

    [Fact]
    public void Non_object_payload_is_rejected()
    {
        var error = Assert.Throws<HttpError>(() =>
            CommentSanitizer.Sanitize(JsonNode.Parse("\"hello\"")));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("Expected a comment payload.", error.Message);
    }

    [Fact]
    public void Non_boolean_manual_is_not_treated_as_manual()
    {
        // JS uses `manual === true`, so a string "true" must NOT enable the manual path.
        var input = JsonNode.Parse("""{"manual":"true","message":"hi"}""");

        var error = Assert.Throws<HttpError>(() => CommentSanitizer.Sanitize(input));

        Assert.Equal("Both authorName and message are required.", error.Message);
    }

    [Fact]
    public void Safe_https_avatar_is_preserved()
    {
        const string url = "https://yt3.ggpht.com/profile=s88-c-k-c0x00ffffff-no-rj?token=abc";
        var input = new JsonObject { ["authorName"] = "@کاربر", ["message"] = "سلام", ["avatarUrl"] = url };

        Assert.Equal(url, CommentSanitizer.Sanitize(input).AvatarUrl);
    }

    [Fact]
    public void Data_image_url_is_preserved()
    {
        const string url = "data:image/png;base64,iVBORw0KGgo=";
        var input = new JsonObject { ["authorName"] = "A", ["message"] = "B", ["avatarUrl"] = url };

        Assert.Equal(url, CommentSanitizer.Sanitize(input).AvatarUrl);
    }

    [Fact]
    public void Strips_control_chars_and_collapses_whitespace()
    {
        // NUL is removed (no space), the run of spaces collapses to one, and the ends are trimmed.
        var message = "  x" + (char)0x00 + "y   z  ";
        var input = new JsonObject { ["authorName"] = "A", ["message"] = message };

        Assert.Equal("xy z", CommentSanitizer.Sanitize(input).Message);
    }

    [Fact]
    public void Preserves_u0085_which_is_not_whitespace_in_javascript()
    {
        // U+0085 (NEL) is whitespace to .NET's \s / Trim() but NOT to JavaScript. The port must keep it.
        var message = "a" + (char)0x85 + "b";
        var input = new JsonObject { ["authorName"] = "A", ["message"] = message };

        Assert.Equal(message, CommentSanitizer.Sanitize(input).Message);
    }
}
