namespace Overlay;

/// <summary>
/// The one and only comment shape the whole system agrees on, matching the Node
/// sanitizer's output: <c>{ authorName, message, avatarUrl, manual }</c>. Serialized
/// with a camelCase policy so the JSON keys match the browser clients byte-for-byte.
/// </summary>
public sealed record Comment(string AuthorName, string Message, string AvatarUrl, bool Manual);

/// <summary>
/// Mirrors the Node pattern of attaching a <c>statusCode</c> to a thrown Error. The
/// top-level request handler maps this to <c>{ "error": message }</c> with the status.
/// </summary>
public sealed class HttpError : Exception
{
    public int StatusCode { get; }

    public HttpError(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
