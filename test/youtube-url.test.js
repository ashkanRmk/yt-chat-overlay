const assert = require("node:assert/strict");
const { test } = require("node:test");

const { extractVideoId, toPopoutChatUrl } = require("../src/youtube-url");

test("turns a bare YouTube video id into a Studio popout chat URL", () => {
  assert.equal(
    toPopoutChatUrl("FkGmLABFJeU"),
    "https://studio.youtube.com/live_chat?is_popout=1&v=FkGmLABFJeU",
  );
});

test("normalizes a Studio live chat URL and forces popout mode", () => {
  assert.equal(
    toPopoutChatUrl("https://studio.youtube.com/live_chat?v=FkGmLABFJeU"),
    "https://studio.youtube.com/live_chat?is_popout=1&v=FkGmLABFJeU",
  );
});

test("extracts the video id from regular YouTube watch and share URLs", () => {
  assert.equal(
    extractVideoId("https://www.youtube.com/watch?v=FkGmLABFJeU&t=10"),
    "FkGmLABFJeU",
  );
  assert.equal(extractVideoId("https://youtu.be/FkGmLABFJeU"), "FkGmLABFJeU");
});

test("rejects input without a valid YouTube video id", () => {
  assert.throws(() => toPopoutChatUrl("not a video"), /valid YouTube video ID/);
});
