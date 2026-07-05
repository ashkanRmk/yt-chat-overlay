# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
cd src && dotnet run                # start the backend (Kestrel) on 127.0.0.1:3000
cd src && PORT=4000 dotnet run       # override the port (host is always 127.0.0.1)
dotnet test                         # backend tests (xUnit + WebApplicationFactory), run from the repo root
npm test                            # run the extension tests (node:test runner)
node --test test/decorator.test.js  # run a single extension test file
```

The backend is a **tiny .NET 10 minimal-API server** (`src/`, Kestrel); it requires the .NET 10 SDK. A root solution (`LiveCommentOverlay.slnx`) covers the app (`src/`) and its tests (`tests/`), so `dotnet build`/`dotnet test` run bare from the repo root; the server itself is launched from `src/` because `dotnet run` targets a single project, not a solution (from the root, `dotnet run --project src` also works). Shared build settings live in `Directory.Build.props`, test package versions in `Directory.Packages.props` (Central Package Management), and `nuget.config` pins restore to nuget.org. The client side — `public/` (overlay + control page) and `extension/` (the Chrome MV3 extension) — is **dependency-free vanilla JS** with no bundler or build step; its unit tests still run under Node's built-in test runner. Keep the client zero-dependency; don't add a client build step without strong reason.

## What this is

A localhost tool for streamers: pick a message from YouTube Live chat and display it as a lower-third card in an OBS browser source. Everything runs on the local machine for a single user — there is no auth, no persistence, and no multi-user concern by design.

## Architecture: the comment's journey

A comment flows through these pieces that only ever talk over `http://127.0.0.1:3000`:

1. **Chrome extension** (`extension/`, MV3) is injected into `studio.youtube.com/live_chat*`. It adds a "Show" button to every chat row and a floating "Clear" button, then `POST`s the selected comment to the local server. It never talks to the overlay directly.
2. **Server** (`src/Program.cs`) is a .NET 10 minimal-API app using standard endpoint routing with DI: `Program.cs` wires the pipeline and maps endpoints defined in `CommentEndpoints`/`PageEndpoints`/`WebSocketEndpoint`. It holds exactly one `currentComment` in memory (inside `OverlayHub`, injected via `IOverlayHub`) — the last one shown. `POST /api/comments/show` sanitizes and stores it, `POST /api/comments/clear` nulls it, `GET /api/comments/current` returns it. A sanitized comment is always `{authorName, message, avatarUrl, manual}`; `manual: true` marks an operator-typed message, which has empty `authorName`/`avatarUrl` and renders on the overlay without the identity pill. Sanitization lives in `src/CommentSanitizer.cs`. JSON responses must stay byte-compatible with the clients: camelCase keys + relaxed escaping (Persian/emoji and `<>&` emitted raw), configured once via `ConfigureHttpJsonOptions` and the shared `JsonSerializerOptions`. Errors return `{error}` — thrown `HttpError`s via `ApiExceptionHandler`, and empty 404/405 routing responses via `StatusPages` — so the control page can still read `.error`.
3. **OverlayHub** (`src/OverlayHub.cs`) tracks the connected WebSocket clients and broadcasts. Kestrel's `UseWebSockets()` performs the RFC 6455 handshake and frame encoding natively (no hand-rolled SHA-1/frame code), so the hub only serializes JSON text frames and hands each new client an `init` message with the current comment on connect. Every show/clear is broadcast to all clients over `/ws`.
4. **Overlay page** (`public/overlay.html` + `assets/overlay.js`) is the OBS browser source. It connects to `/ws`, auto-reconnects on close, and cross-fades between comments. The **control page** (`public/index.html` + `assets/control.js`) is the operator dashboard: it shows a live preview over the same `/ws`, exposes a Clear button, a manual-message form (`POST`s `{message, manual: true}`), and a collapsible "Test messages" fixture panel (see below).

Message protocol over `/ws` is JSON: `{type: "init"|"show"|"clear", comment?}`. Both the overlay and control pages fetch `/api/comments/current` on load *and* listen on the socket — the fetch handles the initial paint, the socket handles updates.

## Conventions and non-obvious details

- **Extension scripts are UMD-wrapped so tests can `require` them.** `comment-extractor.js` and `decorator.js` attach their API to `globalThis`/`window` when loaded as content scripts, and to `module.exports` when loaded under `node --test`. This is why the browser-only extension code has Node-style test files. Keep this dual-mode wrapper intact when editing them, and load order matters (`comment-extractor.js` → `decorator.js` → `content.js`, see `manifest.json`).
- **Avatar inlining.** When a chat row's avatar is a `blob:`/`file:` URL or a YouTube `live_chat_files/` URL (things OBS on another origin can't fetch), the extractor draws it to a canvas and posts a `data:` URL instead. See `shouldInlineAvatar` / `avatarDataUrlFrom` in `extension/comment-extractor.js`. The server-side sanitizer (`src/CommentSanitizer.cs`) only accepts `http(s):` and `data:image/*;base64` avatar URLs.
- **RTL-first.** The overlay is authored for Persian/Arabic: `overlay.html` is `<html dir="rtl">`, the bundled font is Vazirmatn (`public/assets/fonts/`), and `displayAuthorName` in `overlay.js` moves a leading `@` to the end of the handle. Message text itself uses `dir="auto"`. Preserve this when touching overlay layout.
- **Duplicated logic is intentional, not shared.** There is no bundler across the backend/browser/extension boundary, so comment normalization/sanitization is deliberately reimplemented on each side and must be kept in sync: it lives in both `extension/comment-extractor.js` (`normalizeText`, browser) and `src/CommentSanitizer.cs` (backend). The C# sanitizer deliberately avoids .NET's `\s`/`Trim()` — they treat U+0085 (NEL) as whitespace and JavaScript does not — see its comments. Change both copies together when you touch normalization rules.
- **Static file serving.** Assets are served by the standard static-file middleware from two roots, wired in `src/StaticContent.cs` (`/assets/*` → `public/assets`, `/extension/*` → `extension/`); the middleware handles path-traversal protection. The two HTML pages (`/`, `/overlay`) are mapped explicitly in `src/PageEndpoints.cs`. Every asset is sent with `Cache-Control: no-store` (OBS always gets fresh files) and text types carry `charset=utf-8` (the RTL/Persian overlay depends on UTF-8). `FindRepoRoot` walks up from the content root to locate `public/`/`extension/`, so the project moving under `src/` doesn't change what's served. Keep new file routes going through this middleware or an explicit page mapping.
- **The fixture lives inside the control page.** The "Test messages" `<details>` panel in `public/index.html` reproduces YouTube's chat DOM (RTL/LTR, emoji, long messages, missing avatars) so extraction and the injected Show buttons can be tested without going live. For that, the control page loads `extension/content.css`, `comment-extractor.js`, and `decorator.js` — but deliberately **not** `content.js` (it hardcodes port 3000, adds its own floating Clear button, and observes all of `document.body`); `control.js` wires `decorateTree` itself. The fixture rows repeat YouTube's per-row IDs (`#author-name`, `#message`, …) on purpose — only query them scoped to a row.

## Testing

The backend is tested with xUnit + `WebApplicationFactory<Program>` in `tests/` (`dotnet test` from the repo root): it hosts the app in-memory and drives it over HTTP and WebSocket (`ServerTests`, `StaticRoutesTests`), plus direct `CommentSanitizer` unit tests (`SanitizerTests`). Each test spins up a fresh app so the in-memory `currentComment` doesn't leak between tests.

The remaining Node tests (`npm test`, using `node:test` + `node:assert/strict`) cover the client/extension code only: `test/comment-extractor.test.js` and `test/decorator.test.js` run against hand-built fake DOM objects (`fakeRow`, `FakeElement`) rather than a real browser. Mirror that pattern when adding coverage there.
