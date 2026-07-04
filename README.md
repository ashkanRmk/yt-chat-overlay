# YouTube Live Comment Overlay

Local StreamYard-style comment picker for YouTube Live and OBS.

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

## Local Fixture

Use `http://127.0.0.1:3000/fixture` to test extraction, injected buttons, dynamic rows, RTL/LTR text, emoji, long messages, and missing avatars without going live.
