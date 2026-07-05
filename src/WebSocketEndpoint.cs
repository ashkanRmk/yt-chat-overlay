using System.Net.WebSockets;

namespace LiveCommentOverlay;

/// <summary>
/// The <c>/ws</c> endpoint. On a genuine WebSocket upgrade it accepts the socket and hands it to the
/// hub (which sends the <c>init</c> frame and streams broadcasts); a plain HTTP request to <c>/ws</c>
/// gets a 400. The handler returns <see cref="Task"/> (not an <c>IResult</c>) because the response is
/// hijacked once the socket is accepted.
/// </summary>
public static class WebSocketEndpoint
{
    public static IEndpointRouteBuilder MapWebSocketEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/ws", async (HttpContext context, IOverlayHub hub) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.HandleClientAsync(socket, context.RequestAborted);
        });

        return endpoints;
    }
}
