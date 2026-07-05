using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Overlay;

/// <summary>
/// Faithful port of <c>src/comment-sanitizer.js</c>. Every rule (max lengths, the exact
/// character classes and their ordering, the manual-message path, the error messages and 400
/// status) matches the Node original so the two servers accept and reject the same payloads.
/// Character sets are expressed as explicit code points rather than regex escapes to keep the
/// intent unambiguous. In particular U+0085 (NEL) is intentionally NOT treated as whitespace,
/// whereas .NET's built-in \s and string.Trim() would treat it as whitespace and so diverge.
/// </summary>
public static partial class CommentSanitizer
{
    private const int MaxAuthorLength = 80;
    private const int MaxMessageLength = 500;
    private const int MaxAvatarUrlLength = 4096;

    // Accepted inline data URLs (comment-sanitizer.js:20). All-ASCII pattern, so a plain regex is safe.
    [GeneratedRegex(@"^data:image/(?:png|jpe?g|gif|webp);base64,[a-z0-9+/=]+$", RegexOptions.IgnoreCase)]
    private static partial Regex DataImageUrl();

    /// <summary>
    /// Mirrors normalizeText: strip control + zero-width characters, collapse runs of JS-whitespace
    /// to a single space, trim, then cut to <paramref name="maxLength"/> UTF-16 code units. The strip
    /// step runs before the collapse step (as in Node), so VT/FF/ZWNBSP are removed outright rather
    /// than becoming spaces. Collapse+trim are folded into one pass via the pending-space latch.
    /// </summary>
    public static string NormalizeText(string value, int maxLength)
    {
        var input = value ?? "";
        var builder = new StringBuilder(input.Length);
        var started = false;
        var pendingSpace = false;

        foreach (var ch in input)
        {
            if (IsStripped(ch))
            {
                continue;
            }

            if (IsCollapsibleWhitespace(ch))
            {
                if (started)
                {
                    pendingSpace = true; // deferred: trailing runs never get flushed (that is the trim)
                }

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
            started = true;
        }

        var text = builder.ToString();
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    public static string SanitizeAvatarUrl(string value)
    {
        var avatarUrl = TrimJsWhitespace(value ?? "");
        if (avatarUrl.Length == 0 || avatarUrl.Length > MaxAvatarUrlLength)
        {
            return "";
        }

        if (DataImageUrl().IsMatch(avatarUrl))
        {
            return avatarUrl;
        }

        // Node accepts only http(s) and returns the URL normalized by the WHATWG parser.
        // Uri.AbsoluteUri is an extremely close match for real URLs (it lowercases scheme/host,
        // adds a trailing slash to authority-only URLs, etc.); exotic inputs can differ slightly.
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return uri.AbsoluteUri;
        }

        return "";
    }

    public static Comment Sanitize(JsonNode? input)
    {
        // Node: `!input || typeof input !== "object"`. In JS a JSON array is also an object, so
        // it passes this guard and then fails the required-fields check below - we mirror that by
        // treating an array as an object with no readable string fields.
        JsonObject fields;
        if (input is JsonObject obj)
        {
            fields = obj;
        }
        else if (input is JsonArray)
        {
            fields = new JsonObject();
        }
        else
        {
            throw new HttpError(400, "Expected a comment payload.");
        }

        var manual = fields["manual"] is JsonValue manualValue
            && manualValue.TryGetValue<bool>(out var manualBool)
            && manualBool;
        var authorName = NormalizeText(Coerce(fields["authorName"]), MaxAuthorLength);
        var message = NormalizeText(Coerce(fields["message"]), MaxMessageLength);

        if (manual)
        {
            if (message.Length == 0)
            {
                throw new HttpError(400, "A message is required.");
            }

            return new Comment("", message, "", true);
        }

        if (authorName.Length == 0 || message.Length == 0)
        {
            throw new HttpError(400, "Both authorName and message are required.");
        }

        return new Comment(authorName, message, SanitizeAvatarUrl(Coerce(fields["avatarUrl"])), false);
    }

    // Replicates JavaScript's `String(value || "")` for the field types a JSON body can hold.
    // Objects/arrays (which JS would stringify to "[object Object]" / joined elements) are treated
    // as "" - no client sends those, so the exotic coercion is not reproduced.
    private static string Coerce(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return "";
        }

        if (value.TryGetValue<string>(out var s))
        {
            return s ?? "";
        }

        if (value.TryGetValue<bool>(out var b))
        {
            return b ? "true" : ""; // true || "" => "true"; false || "" => ""
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d == 0 || double.IsNaN(d) ? "" : d.ToString(CultureInfo.InvariantCulture);
        }

        return "";
    }

    // Control + zero-width chars removed outright by normalizeText (comment-sanitizer.js:7-8):
    // U+0000..U+0008, U+000B, U+000C, U+000E..U+001F, U+007F, U+200B, U+FEFF.
    // NOTE: U+0009 (tab), U+000A (LF), U+000D (CR) are NOT here - they are whitespace (collapsed below).
    private static bool IsStripped(char ch)
    {
        int u = ch;
        if (u <= 0x08)
        {
            return true;
        }

        if (u is 0x0B or 0x0C)
        {
            return true;
        }

        if (u is >= 0x0E and <= 0x1F)
        {
            return true;
        }

        return u is 0x7F or 0x200B or 0xFEFF;
    }

    // The JS \s set MINUS the code points already removed by IsStripped (U+000B/U+000C/U+FEFF).
    // These collapse to a single space. U+0085 (NEL) is intentionally absent.
    private static bool IsCollapsibleWhitespace(char ch) => (int)ch switch
    {
        0x09 or 0x0A or 0x0D or 0x20 or 0xA0 or 0x1680 => true,
        >= 0x2000 and <= 0x200A => true,
        0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        _ => false,
    };

    // The set removed by JS String.prototype.trim(): the collapsible set plus U+000B/U+000C/U+FEFF.
    private static bool IsTrimWhitespace(char ch) =>
        IsCollapsibleWhitespace(ch) || (int)ch is 0x0B or 0x0C or 0xFEFF;

    private static string TrimJsWhitespace(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsTrimWhitespace(s[start]))
        {
            start++;
        }

        while (end > start && IsTrimWhitespace(s[end - 1]))
        {
            end--;
        }

        return s.Substring(start, end - start);
    }
}
