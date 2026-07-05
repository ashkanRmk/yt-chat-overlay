# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
dotnet run --project server-dotnet             # start the backend (Kestrel) on 127.0.0.1:3000
PORT=4000 dotnet run --project server-dotnet    # override the port (host is always 127.0.0.1)
dotnet test server-dotnet.Tests                 # backend tests (xUnit + WebApplicationFactory)
npm test                                        # run the extension tests (node:test runner)
node --test test/decorator.test.js              # run a single extension test file
```

The backend is a **tiny .NET 10 minimal-API server** (`server-dotnet/`, Kestrel); it requires the .NET 10 SDK. The client side — `public/` (overlay + control page) and `extension/` (the Chrome MV3 extension) — is **dependency-free vanilla JS** with no bundler or build step; its unit tests still run under Node's built-in test runner. Keep the client zero-dependency; don't add a client build step without strong reason.

## What this is

A localhost tool for streamers: pick a message from YouTube Live chat and display it as a lower-third card in an OBS browser source. Everything runs on the local machine for a single user — there is no auth, no persistence, and no multi-user concern by design.

## Architecture: the comment's journey

A comment flows through these pieces that only ever talk over `http://127.0.0.1:3000`:

1. **Chrome extension** (`extension/`, MV3) is injected into `studio.youtube.com/live_chat*`. It adds a "Show" button to every chat row and a floating "Clear" button, then `POST`s the selected comment to the local server. It never talks to the overlay directly.
2. **Server** (`server-dotnet/Program.cs`) is a .NET 10 minimal-API app whose single terminal request handler mirrors a hand-rolled router (case-sensitive matching on method + path, no MVC/endpoint routing) so its behavior stays byte-predictable. It holds exactly one `currentComment` in memory (inside `OverlayHub`) — the last one shown. `POST /api/comments/show` sanitizes and stores it, `POST /api/comments/clear` nulls it, `GET /api/comments/current` returns it. A sanitized comment is always `{authorName, message, avatarUrl, manual}`; `manual: true` marks an operator-typed message, which has empty `authorName`/`avatarUrl` and renders on the overlay without the identity pill. Sanitization lives in `server-dotnet/CommentSanitizer.cs`.
3. **OverlayHub** (`server-dotnet/OverlayHub.cs`) tracks the connected WebSocket clients and broadcasts. Kestrel's `UseWebSockets()` performs the RFC 6455 handshake and frame encoding natively (no hand-rolled SHA-1/frame code), so the hub only serializes JSON text frames and hands each new client an `init` message with the current comment on connect. Every show/clear is broadcast to all clients over `/ws`.
4. **Overlay page** (`public/overlay.html` + `assets/overlay.js`) is the OBS browser source. It connects to `/ws`, auto-reconnects on close, and cross-fades between comments. The **control page** (`public/index.html` + `assets/control.js`) is the operator dashboard: it shows a live preview over the same `/ws`, exposes a Clear button, a manual-message form (`POST`s `{message, manual: true}`), and a collapsible "Test messages" fixture panel (see below).

Message protocol over `/ws` is JSON: `{type: "init"|"show"|"clear", comment?}`. Both the overlay and control pages fetch `/api/comments/current` on load *and* listen on the socket — the fetch handles the initial paint, the socket handles updates.

## Conventions and non-obvious details

- **Extension scripts are UMD-wrapped so tests can `require` them.** `comment-extractor.js` and `decorator.js` attach their API to `globalThis`/`window` when loaded as content scripts, and to `module.exports` when loaded under `node --test`. This is why the browser-only extension code has Node-style test files. Keep this dual-mode wrapper intact when editing them, and load order matters (`comment-extractor.js` → `decorator.js` → `content.js`, see `manifest.json`).
- **Avatar inlining.** When a chat row's avatar is a `blob:`/`file:` URL or a YouTube `live_chat_files/` URL (things OBS on another origin can't fetch), the extractor draws it to a canvas and posts a `data:` URL instead. See `shouldInlineAvatar` / `avatarDataUrlFrom` in `extension/comment-extractor.js`. The server-side sanitizer (`server-dotnet/CommentSanitizer.cs`) only accepts `http(s):` and `data:image/*;base64` avatar URLs.
- **RTL-first.** The overlay is authored for Persian/Arabic: `overlay.html` is `<html dir="rtl">`, the bundled font is Vazirmatn (`public/assets/fonts/`), and `displayAuthorName` in `overlay.js` moves a leading `@` to the end of the handle. Message text itself uses `dir="auto"`. Preserve this when touching overlay layout.
- **Duplicated logic is intentional, not shared.** There is no bundler across the backend/browser/extension boundary, so comment normalization/sanitization is deliberately reimplemented on each side and must be kept in sync: it lives in both `extension/comment-extractor.js` (`normalizeText`, browser) and `server-dotnet/CommentSanitizer.cs` (backend). The C# sanitizer deliberately avoids .NET's `\s`/`Trim()` — they treat U+0085 (NEL) as whitespace and JavaScript does not — see its comments. Change both copies together when you touch normalization rules.
- **Static file serving is locked down.** `ServeFromRoot` in `server-dotnet/Program.cs` resolves and then rejects any path that escapes its root (`/assets/*` → `public/assets`, `/extension/*` → `extension/`). Named routes (`/`, `/overlay`) map to specific files. Keep new file routes going through `ServeFromRoot` or an explicit allowlist.
- **The fixture lives inside the control page.** The "Test messages" `<details>` panel in `public/index.html` reproduces YouTube's chat DOM (RTL/LTR, emoji, long messages, missing avatars) so extraction and the injected Show buttons can be tested without going live. For that, the control page loads `extension/content.css`, `comment-extractor.js`, and `decorator.js` — but deliberately **not** `content.js` (it hardcodes port 3000, adds its own floating Clear button, and observes all of `document.body`); `control.js` wires `decorateTree` itself. The fixture rows repeat YouTube's per-row IDs (`#author-name`, `#message`, …) on purpose — only query them scoped to a row.

## Testing

The backend is tested with xUnit + `WebApplicationFactory<Program>` in `server-dotnet.Tests/` (`dotnet test server-dotnet.Tests`): it hosts the app in-memory and drives it over HTTP and WebSocket (`ServerTests`, `StaticRoutesTests`), plus direct `CommentSanitizer` unit tests (`SanitizerTests`). Each test spins up a fresh app so the in-memory `currentComment` doesn't leak between tests.

The remaining Node tests (`npm test`, using `node:test` + `node:assert/strict`) cover the client/extension code only: `test/comment-extractor.test.js` and `test/decorator.test.js` run against hand-built fake DOM objects (`fakeRow`, `FakeElement`) rather than a real browser. Mirror that pattern when adding coverage there.
