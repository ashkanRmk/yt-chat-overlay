using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveCommentOverlay;

/// <summary>
/// Reads a request body as a raw <see cref="JsonNode"/>, enforcing the same 64 KiB cap and the same
/// empty-body/invalid-JSON handling as the Node server. Reading the body as a raw node (rather than
/// binding to a typed model) is deliberate: <see cref="CommentSanitizer"/> applies JavaScript-style
/// coercion (numbers/bools become strings, arrays become empty objects) that typed binding would reject.
/// </summary>
public static class JsonBody
{
    public static async Task<JsonNode?> ReadAsync(HttpRequest request, long maxBytes)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        long size = 0;
        int read;
        while ((read = await request.Body.ReadAsync(buffer)) > 0)
        {
            size += read;
            if (size > maxBytes)
            {
                throw new HttpError(StatusCodes.Status413PayloadTooLarge, "Request body is too large.");
            }

            memory.Write(buffer, 0, read);
        }

        var raw = Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
        if (raw.Length == 0)
        {
            return new JsonObject(); // empty body -> {}
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            throw new HttpError(StatusCodes.Status400BadRequest, "Request body must be valid JSON.");
        }
    }
}
