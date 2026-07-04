# YouTube Live Comment Overlay

Local StreamYard-style comment picker for YouTube Live and OBS.

## What is this?

A localhost tool for streamers: pick a message from YouTube Live chat and display it as a lower-third card in an OBS browser source. Everything runs on the local machine for a single user — there is no auth, no persistence, and no multi-user concern by design.

## Run

```sh
npm run dev
```

Open `http://127.0.0.1:3000/`.

## Chrome Extension

1. Open `chrome://extensions`.
2. Enable Developer Mode.
3. Load unpacked extension from this repo's `extension/` folder.
4. Open a YouTube Studio popout chat URL from the control page.

The extension only matches `https://studio.youtube.com/live_chat*` and posts selected plain-text comments to `http://127.0.0.1:3000`.

## OBS

Add a Browser Source:

- URL: `http://127.0.0.1:3000/overlay`
- Width: `1920`
- Height: `1080`
- Background: transparent

Click `Show` on a regular YouTube text chat row to display it. Click another `Show` to replace it, or `Clear` to fade it out.

## Manual Messages

The control page has a "Manual message" box: type any text and click `Show on Overlay`. Manual messages render on the overlay without the avatar/username pill — message text only.

## Test Messages

Expand the "Test messages" panel on the control page (`http://127.0.0.1:3000/`) to test extraction, injected buttons, dynamic rows, RTL/LTR text, emoji, long messages, and missing avatars without going live.
