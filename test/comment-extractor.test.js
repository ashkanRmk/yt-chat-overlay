const assert = require("node:assert/strict");
const { test } = require("node:test");

const { extractComment, extractCommentForPosting, normalizeText } = require("../extension/comment-extractor");

function fakeRow({
  authorName,
  message,
  hoverMessage,
  currentSrc,
  src,
  rawSrc,
  srcset,
  dataThumb,
  baseURI,
  canvasDataUrl,
}) {
  const avatar =
    currentSrc || src || rawSrc || srcset || dataThumb
      ? {
          currentSrc,
          src,
          complete: true,
          naturalHeight: 24,
          naturalWidth: 24,
          height: 24,
          width: 24,
          getAttribute(name) {
            if (name === "src") {
              return rawSrc;
            }
            if (name === "srcset") {
              return srcset;
            }
            if (name === "data-thumb") {
              return dataThumb;
            }
            return "";
          },
        }
      : null;

  return {
    ownerDocument: {
      baseURI: baseURI || "https://studio.youtube.com/live_chat?is_popout=1&v=abc123abc12",
      createElement(tagName) {
        if (tagName !== "canvas") {
          return {};
        }
        return {
          height: 0,
          width: 0,
          getContext(type) {
            assert.equal(type, "2d");
            return {
              drawImage() {},
            };
          },
          toDataURL(type) {
            assert.equal(type, "image/png");
            return canvasDataUrl;
          },
        };
      },
    },
    querySelector(selector) {
      if (selector === "#author-name") {
        return { textContent: authorName };
      }
      if (selector === "#message") {
        return { textContent: message };
      }
      if (selector === "#hover-message") {
        return hoverMessage === undefined ? null : { textContent: hoverMessage };
      }
      if (
        selector === "#author-photo img" ||
        selector === "yt-img-shadow#author-photo img" ||
        selector === "#author-photo [src]" ||
        selector === "#author-photo [data-thumb]"
      ) {
        return avatar;
      }
      return null;
    },
  };
}

test("normalizes YouTube chat text without removing Persian, English, or emoji", () => {
  assert.equal(normalizeText("\u200b  سلام  test 😀  "), "سلام test 😀");
});

test("extracts plain author, message, and avatar URL from a chat row", () => {
  assert.deepEqual(
    extractComment(
      fakeRow({
        authorName: "  @HappyDeveloper  ",
        message: " <b>plain text only</b> ",
        currentSrc: "https://yt.example/avatar.jpg",
      }),
    ),
    {
      authorName: "@HappyDeveloper",
      message: "<b>plain text only</b>",
      avatarUrl: "https://yt.example/avatar.jpg",
    },
  );
});

test("prefers YouTube hover-message text when it is available", () => {
  assert.equal(
    extractComment(
      fakeRow({
        authorName: "@HappyDeveloper",
        message: "Original untranslated message",
        hoverMessage: "سلام پیام تستی",
      }),
    ).message,
    "سلام پیام تستی",
  );
});

test("falls back to img src and returns an empty avatar when no image exists", () => {
  assert.equal(
    extractComment(
      fakeRow({
        authorName: "A",
        message: "B",
        src: "https://yt.example/fallback.png",
      }),
    ).avatarUrl,
    "https://yt.example/fallback.png",
  );

  assert.equal(
    extractComment(fakeRow({ authorName: "A", message: "B" })).avatarUrl,
    "",
  );
});

test("resolves relative avatar URLs against the chat document", () => {
  assert.equal(
    extractComment(
      fakeRow({
        authorName: "A",
        message: "B",
        rawSrc: "./live_chat_files/channels4_profile.jpg",
        baseURI: "https://studio.youtube.com/live_chat?is_popout=1&v=FkGmLABFJeU",
      }),
    ).avatarUrl,
    "https://studio.youtube.com/live_chat_files/channels4_profile.jpg",
  );
});

test("extracts avatar URLs from data-thumb and srcset when direct src is unavailable", () => {
  assert.equal(
    extractComment(
      fakeRow({
        authorName: "A",
        message: "B",
        dataThumb: "https://yt.example/thumb.jpg",
      }),
    ).avatarUrl,
    "https://yt.example/thumb.jpg",
  );

  assert.equal(
    extractComment(
      fakeRow({
        authorName: "A",
        message: "B",
        srcset: "https://yt.example/one.jpg 1x, https://yt.example/two.jpg 2x",
      }),
    ).avatarUrl,
    "https://yt.example/one.jpg",
  );
});

test("inlines saved YouTube chat avatar files before posting to OBS", async () => {
  const comment = await extractCommentForPosting(
    fakeRow({
      authorName: "A",
      message: "B",
      rawSrc: "./live_chat_files/channels4_profile.jpg",
      src: "http://127.0.0.1:3000/live_chat_files/channels4_profile.jpg",
      canvasDataUrl: "data:image/png;base64,aGVsbG8=",
      baseURI: "http://127.0.0.1:3000/live_chat.html",
    }),
  );

  assert.equal(comment.avatarUrl, "data:image/png;base64,aGVsbG8=");
});

test("returns null for rows without a visible author or message", () => {
  assert.equal(extractComment(fakeRow({ authorName: "", message: "hello" })), null);
  assert.equal(extractComment(fakeRow({ authorName: "Sara", message: "   " })), null);
});
