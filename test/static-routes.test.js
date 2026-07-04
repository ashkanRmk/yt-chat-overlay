const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");

const { createOverlayServer } = require("../src/server");

async function startTestServer() {
  const app = createOverlayServer();
  await new Promise((resolve) => app.server.listen(0, "127.0.0.1", resolve));
  const { port } = app.server.address();
  return {
    baseUrl: `http://127.0.0.1:${port}`,
    close: () => new Promise((resolve) => app.server.close(resolve)),
  };
}

test("serves the control, overlay, and local fixture pages", async (t) => {
  const server = await startTestServer();
  t.after(server.close);

  const control = await fetch(`${server.baseUrl}/`);
  assert.equal(control.status, 200);
  assert.match(await control.text(), /YouTube Live Comment Overlay/);

  const overlay = await fetch(`${server.baseUrl}/overlay`);
  assert.equal(overlay.status, 200);
  const overlayHtml = await overlay.text();
  assert.match(overlayHtml, /comment-card/);
  assert.match(overlayHtml, /<html lang="en" dir="rtl">/);
  assert.match(overlayHtml, /id="comment-card"[^>]+dir="rtl"/);
  assert.match(overlayHtml, /id="author-name"[^>]+dir="rtl"/);

  const fixture = await fetch(`${server.baseUrl}/fixture`);
  assert.equal(fixture.status, 200);
  assert.match(await fixture.text(), /yt-live-chat-text-message-renderer/);

  const appCss = await fetch(`${server.baseUrl}/assets/app.css`);
  assert.equal(appCss.status, 200);
  assert.match(await appCss.text(), /app-shell/);

  const overlayJs = await fetch(`${server.baseUrl}/assets/overlay.js`);
  assert.equal(overlayJs.status, 200);
  assert.match(await overlayJs.text(), /initOverlay/);

  const overlayCss = await fetch(`${server.baseUrl}/assets/overlay.css`);
  assert.equal(overlayCss.status, 200);
  const overlayCssText = await overlayCss.text();
  assert.match(overlayCssText, /font-family:\s*"Vazir"/);
  assert.match(overlayCssText, /direction:\s*rtl/);

  const vazirFont = await fetch(`${server.baseUrl}/assets/fonts/Vazirmatn.woff2`);
  assert.equal(vazirFont.status, 200);
  assert.match(vazirFont.headers.get("content-type"), /font\/woff2/);
});

test("overlay replacement fades out before rendering the next comment", () => {
  const overlayJs = fs.readFileSync(
    path.join(__dirname, "..", "public", "assets", "overlay.js"),
    "utf8",
  );

  assert.match(overlayJs, /function transitionToComment\(comment\)/);
  assert.match(overlayJs, /card\.classList\.remove\("is-visible"\)/);
  assert.match(overlayJs, /window\.setTimeout\(\(\) => \{\s*renderComment\(comment\)/);
});

test("overlay renders leading handles with the at sign on the visual left", () => {
  const overlayJs = fs.readFileSync(
    path.join(__dirname, "..", "public", "assets", "overlay.js"),
    "utf8",
  );

  assert.match(overlayJs, /function displayAuthorName\(name\)/);
  assert.match(overlayJs, /return `\$\{withoutAt\}@`;/);
  assert.match(overlayJs, /authorName\.textContent = displayAuthorName\(comment\.authorName\)/);
});
