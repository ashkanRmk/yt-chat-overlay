const VIDEO_ID_PATTERN = /^[A-Za-z0-9_-]{11}$/;

function assertValidVideoId(videoId) {
  if (!VIDEO_ID_PATTERN.test(videoId)) {
    throw new Error("Please enter a valid YouTube video ID or chat URL.");
  }
  return videoId;
}

function extractVideoId(input) {
  const value = String(input || "").trim();
  if (VIDEO_ID_PATTERN.test(value)) {
    return value;
  }

  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error("Please enter a valid YouTube video ID or chat URL.");
  }

  const host = url.hostname.replace(/^www\./, "");
  let videoId = "";

  if (host === "youtu.be") {
    videoId = url.pathname.split("/").filter(Boolean)[0] || "";
  } else if (host.endsWith("youtube.com") || host === "studio.youtube.com") {
    videoId = url.searchParams.get("v") || "";

    if (!videoId && url.pathname.startsWith("/shorts/")) {
      videoId = url.pathname.split("/").filter(Boolean)[1] || "";
    }

    if (!videoId && url.pathname.startsWith("/embed/")) {
      videoId = url.pathname.split("/").filter(Boolean)[1] || "";
    }
  }

  return assertValidVideoId(videoId);
}

function toPopoutChatUrl(input) {
  const videoId = extractVideoId(input);
  const url = new URL("https://studio.youtube.com/live_chat");
  url.searchParams.set("is_popout", "1");
  url.searchParams.set("v", videoId);
  return url.toString();
}

module.exports = {
  extractVideoId,
  toPopoutChatUrl,
};
