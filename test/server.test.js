const assert = require("node:assert/strict");
const { test } = require("node:test");

const { createOverlayServer } = require("../src/server");

async function startTestServer() {
  const app = createOverlayServer();
  await new Promise((resolve) => app.server.listen(0, "127.0.0.1", resolve));
  const { port } = app.server.address();
  return {
    app,
    baseUrl: `http://127.0.0.1:${port}`,
    wsUrl: `ws://127.0.0.1:${port}/ws`,
    close: () => new Promise((resolve) => app.server.close(resolve)),
  };
}

async function closeSocket(socket) {
  if (!socket || socket.readyState === WebSocket.CLOSED) {
    return;
  }

  await new Promise((resolve) => {
    const timer = setTimeout(resolve, 100);
    socket.addEventListener(
      "close",
      () => {
        clearTimeout(timer);
        resolve();
      },
      { once: true },
    );
    socket.close();
  });
}

function waitForSocketEvent(socket, expectedType) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`Timed out waiting for ${expectedType}`)), 1000);
    socket.addEventListener("message", function onMessage(event) {
      const payload = JSON.parse(event.data);
      if (payload.type !== expectedType) {
        return;
      }
      clearTimeout(timer);
      socket.removeEventListener("message", onMessage);
      resolve(payload);
    });
  });
}

test("POST /api/comments/show stores a sanitized comment and broadcasts it", async (t) => {
  const server = await startTestServer();
  const socket = new WebSocket(server.wsUrl);
  await new Promise((resolve) => socket.addEventListener("open", resolve, { once: true }));
  t.after(async () => {
    await closeSocket(socket);
    await server.close();
  });

  const showEvent = waitForSocketEvent(socket, "show");
  const response = await fetch(`${server.baseUrl}/api/comments/show`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      authorName: "  Sara <script>  ",
      message: "  سلام  test 😀  ",
      avatarUrl: "javascript:alert(1)",
    }),
  });

  assert.equal(response.status, 200);
  const body = await response.json();
  assert.deepEqual(body.comment, {
    authorName: "Sara <script>",
    message: "سلام test 😀",
    avatarUrl: "",
  });

  assert.deepEqual((await showEvent).comment, body.comment);

  const current = await (await fetch(`${server.baseUrl}/api/comments/current`)).json();
  assert.deepEqual(current.comment, body.comment);
});

test("POST /api/comments/clear clears state and broadcasts clear", async (t) => {
  const server = await startTestServer();

  await fetch(`${server.baseUrl}/api/comments/show`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ authorName: "A", message: "B", avatarUrl: "" }),
  });

  const socket = new WebSocket(server.wsUrl);
  await new Promise((resolve) => socket.addEventListener("open", resolve, { once: true }));
  t.after(async () => {
    await closeSocket(socket);
    await server.close();
  });

  const clearEvent = waitForSocketEvent(socket, "clear");
  const response = await fetch(`${server.baseUrl}/api/comments/clear`, { method: "POST" });

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true, comment: null });
  assert.deepEqual(await clearEvent, { type: "clear" });

  const current = await (await fetch(`${server.baseUrl}/api/comments/current`)).json();
  assert.deepEqual(current, { comment: null });
});

test("POST /api/comments/show preserves safe absolute avatar URLs", async (t) => {
  const server = await startTestServer();
  t.after(async () => {
    await server.close();
  });

  const avatarUrl = "https://yt3.ggpht.com/profile=s88-c-k-c0x00ffffff-no-rj?token=abc";
  const response = await fetch(`${server.baseUrl}/api/comments/show`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      authorName: "@کاربر",
      message: "سلام",
      avatarUrl,
    }),
  });

  assert.equal(response.status, 200);
  assert.equal((await response.json()).comment.avatarUrl, avatarUrl);
});
