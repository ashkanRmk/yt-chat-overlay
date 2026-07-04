# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
npm run dev            # start the server (node src/server.js) on 127.0.0.1:3000
npm test               # run every test/*.test.js via the node:test runner
node --test test/server.test.js          # run a single test file
node --test --test-name-pattern="clear"  # run tests whose name matches
PORT=4000 npm run dev  # override the port (host is always 127.0.0.1)
```

Requires Node >= 22. **There are no dependencies** — no `npm install`, no build step, no linter/formatter config. Everything is Node core modules and vanilla browser JS. Do not add a dependency without strong reason; the zero-dependency property is intentional.

## What this is

A localhost tool for streamers: pick a message from YouTube Live chat and display it as a lower-third card in an OBS browser source. Everything runs on the local machine for a single user — there is no auth, no persistence, and no multi-user concern by design.

## Architecture: the comment's journey

A comment flows through four processes that only ever talk over `http://127.0.0.1:3000`:

1. **Chrome extension** (`extension/`, MV3) is injected into `studio.youtube.com/live_chat*`. It adds a "Show" button to every chat row and a floating "Clear" button, then `POST`s the selected comment to the local server. It never talks to the overlay directly.
2. **Server** (`src/server.js`) is a single `http.createServer` with a hand-rolled router (string matching on method + `url.pathname`, no framework). It holds exactly one `currentComment` in memory — the last one shown. `POST /api/comments/show` sanitizes and stores it, `POST /api/comments/clear` nulls it, `GET /api/comments/current` returns it.
3. **WebSocketHub** (`src/websocket-hub.js`) is a from-scratch WebSocket server (SHA-1 handshake + manual frame encoding in `encodeFrame`) attached via the server's `upgrade` event on `/ws`. Every show/clear is `broadcast()` to all connected clients. New clients get an `init` message with the current comment on connect.
4. **Overlay page** (`public/overlay.html` + `assets/overlay.js`) is the OBS browser source. It connects to `/ws`, auto-reconnects on close, and cross-fades between comments. The **control page** (`public/index.html` + `assets/control.js`) is the operator dashboard: it opens the YouTube popout chat, shows a live preview over the same `/ws`, and exposes a Clear button.

Message protocol over `/ws` is JSON: `{type: "init"|"show"|"clear", comment?}`. Both the overlay and control pages fetch `/api/comments/current` on load *and* listen on the socket — the fetch handles the initial paint, the socket handles updates.

## Conventions and non-obvious details

- **Extension scripts are UMD-wrapped so tests can `require` them.** `comment-extractor.js` and `decorator.js` attach their API to `globalThis`/`window` when loaded as content scripts, and to `module.exports` when loaded under `node --test`. This is why the browser-only extension code has Node-style test files. Keep this dual-mode wrapper intact when editing them, and load order matters (`comment-extractor.js` → `decorator.js` → `content.js`, see `manifest.json`).
- **Avatar inlining.** When a chat row's avatar is a `blob:`/`file:` URL or a YouTube `live_chat_files/` URL (things OBS on another origin can't fetch), the extractor draws it to a canvas and posts a `data:` URL instead. See `shouldInlineAvatar` / `avatarDataUrlFrom` in `extension/comment-extractor.js`. The server-side sanitizer (`src/comment-sanitizer.js`) only accepts `http(s):` and `data:image/*;base64` avatar URLs.
- **RTL-first.** The overlay is authored for Persian/Arabic: `overlay.html` is `<html dir="rtl">`, the bundled font is Vazirmatn (`public/assets/fonts/`), and `displayAuthorName` in `overlay.js` moves a leading `@` to the end of the handle. Message text itself uses `dir="auto"`. Preserve this when touching overlay layout.
- **Duplicated logic is intentional, not shared.** YouTube URL parsing exists in both `src/youtube-url.js` (server/tests) and inline in `assets/control.js` (browser), and `normalizeText` exists in both `src/comment-sanitizer.js` and `extension/comment-extractor.js`. There is no bundler to share modules across the server/browser/extension boundary, so if you change parsing or normalization rules, update every copy.
- **Static file serving is locked down.** `serveFromRoot` in `src/server.js` resolves and then rejects any path that escapes its root (`/assets/*` → `public/assets`, `/extension/*` → `extension/`). Named routes (`/`, `/overlay`, `/fixture`) map to specific files. Keep new file routes going through `serveFromRoot` or an explicit allowlist.
- **`GET /fixture`** serves `fixtures/live-chat-fixture.html`, a standalone reproduction of YouTube's chat DOM (RTL/LTR, emoji, long messages, missing avatars) for testing extraction and the injected buttons without going live.

## Testing

Tests use only `node:test` + `node:assert/strict`. Server tests (`test/server.test.js`, `test/static-routes.test.js`) start the real server on port `0` and hit it over HTTP/WebSocket. Extension tests (`test/comment-extractor.test.js`, `test/decorator.test.js`) run against hand-built fake DOM objects (`fakeRow`, `FakeElement`) rather than a real browser — mirror that pattern when adding coverage there.
