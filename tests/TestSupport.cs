using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace LiveCommentOverlay.Tests;

internal static class TestSupport
{
    public static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    public static async Task<JsonNode?> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    public static async Task<WebSocket> ConnectWebSocket(WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        return await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
    }

    public static async Task<JsonNode> ReceiveJson(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        var bytes = new List<byte>();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            bytes.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return JsonNode.Parse(Encoding.UTF8.GetString(bytes.ToArray()))!;
    }
}
