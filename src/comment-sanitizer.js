const MAX_AUTHOR_LENGTH = 80;
const MAX_MESSAGE_LENGTH = 500;
const MAX_AVATAR_URL_LENGTH = 4096;

function normalizeText(value, maxLength = MAX_MESSAGE_LENGTH) {
  return String(value || "")
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "")
    .replace(/[\u200b\ufeff]/g, "")
    .replace(/\s+/gu, " ")
    .trim()
    .slice(0, maxLength);
}

function sanitizeAvatarUrl(value) {
  const avatarUrl = String(value || "").trim();
  if (!avatarUrl || avatarUrl.length > MAX_AVATAR_URL_LENGTH) {
    return "";
  }

  if (/^data:image\/(?:png|jpe?g|gif|webp);base64,[a-z0-9+/=]+$/i.test(avatarUrl)) {
    return avatarUrl;
  }

  try {
    const url = new URL(avatarUrl);
    if (url.protocol === "http:" || url.protocol === "https:") {
      return url.toString();
    }
  } catch {
    return "";
  }

  return "";
}

function sanitizeComment(input) {
  if (!input || typeof input !== "object") {
    const error = new Error("Expected a comment payload.");
    error.statusCode = 400;
    throw error;
  }

  const manual = input.manual === true;
  const authorName = normalizeText(input.authorName, MAX_AUTHOR_LENGTH);
  const message = normalizeText(input.message, MAX_MESSAGE_LENGTH);

  if (manual) {
    if (!message) {
      const error = new Error("A message is required.");
      error.statusCode = 400;
      throw error;
    }

    return {
      authorName: "",
      message,
      avatarUrl: "",
      manual: true,
    };
  }

  if (!authorName || !message) {
    const error = new Error("Both authorName and message are required.");
    error.statusCode = 400;
    throw error;
  }

  return {
    authorName,
    message,
    avatarUrl: sanitizeAvatarUrl(input.avatarUrl),
    manual: false,
  };
}

module.exports = {
  normalizeText,
  sanitizeAvatarUrl,
  sanitizeComment,
};
