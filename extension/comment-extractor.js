(function attachCommentExtractor(root, factory) {
  const api = factory();

  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }

  root.LiveCommentExtractor = api;
})(typeof globalThis !== "undefined" ? globalThis : window, function createCommentExtractor() {
  function normalizeText(value) {
    return String(value || "")
      .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "")
      .replace(/[\u200b\ufeff]/g, "")
      .replace(/\s+/gu, " ")
      .trim();
  }

  function firstAvailableElement(row, selectors) {
    for (const selector of selectors) {
      const element = row.querySelector(selector);
      if (element) {
        return element;
      }
    }
    return null;
  }

  function firstSrcsetUrl(srcset) {
    return normalizeText(srcset).split(",")[0]?.trim().split(/\s+/u)[0] || "";
  }

  function avatarElementFrom(row) {
    return firstAvailableElement(row, [
      "#author-photo img",
      "yt-img-shadow#author-photo img",
      "#author-photo [src]",
      "#author-photo [data-thumb]",
    ]);
  }

  function rawAvatarCandidates(avatarElement) {
    if (!avatarElement) {
      return [];
    }

    return [
      avatarElement.currentSrc,
      avatarElement.src,
      avatarElement.getAttribute && avatarElement.getAttribute("src"),
      avatarElement.getAttribute && avatarElement.getAttribute("data-thumb"),
      firstSrcsetUrl(avatarElement.getAttribute && avatarElement.getAttribute("srcset")),
    ];
  }

  function isPostableAvatarUrl(value) {
    const raw = normalizeText(value);
    if (!raw) {
      return false;
    }

    if (/^data:image\//i.test(raw)) {
      return true;
    }

    try {
      const url = new URL(raw);
      return url.protocol === "http:" || url.protocol === "https:";
    } catch {
      return false;
    }
  }

  function resolveAvatarUrl(value, row) {
    const raw = normalizeText(value);
    if (!raw) {
      return "";
    }

    if (/^data:image\//i.test(raw)) {
      return raw;
    }

    try {
      return new URL(raw, row.ownerDocument && row.ownerDocument.baseURI).toString();
    } catch {
      return raw;
    }
  }

  function avatarUrlFrom(row) {
    const avatarElement = avatarElementFrom(row);
    if (!avatarElement) {
      return "";
    }

    for (const candidate of rawAvatarCandidates(avatarElement)) {
      const avatarUrl = resolveAvatarUrl(candidate, row);
      if (isPostableAvatarUrl(avatarUrl)) {
        return avatarUrl;
      }
    }

    return "";
  }

  function shouldInlineAvatar(row, avatarElement, avatarUrl) {
    const candidates = rawAvatarCandidates(avatarElement).map(normalizeText);
    return candidates.some((candidate) => {
      if (!candidate) {
        return false;
      }

      if (/^(?:file|blob):/i.test(candidate)) {
        return true;
      }

      if (candidate.includes("live_chat_files/")) {
        return true;
      }

      try {
        const url = new URL(candidate, row.ownerDocument && row.ownerDocument.baseURI);
        return (
          (url.hostname === "127.0.0.1" || url.hostname === "localhost") &&
          url.pathname.includes("/live_chat_files/")
        );
      } catch {
        return false;
      }
    }) || !isPostableAvatarUrl(avatarUrl);
  }

  function waitForImageReady(image) {
    if (image.complete && image.naturalWidth > 0 && image.naturalHeight > 0) {
      return Promise.resolve();
    }

    if (typeof image.decode === "function") {
      return Promise.race([
        image.decode().catch(() => {}),
        new Promise((resolve) => setTimeout(resolve, 400)),
      ]);
    }

    return new Promise((resolve) => {
      const timer = setTimeout(resolve, 400);
      image.addEventListener &&
        image.addEventListener(
          "load",
          () => {
            clearTimeout(timer);
            resolve();
          },
          { once: true },
        );
      image.addEventListener &&
        image.addEventListener(
          "error",
          () => {
            clearTimeout(timer);
            resolve();
          },
          { once: true },
        );
    });
  }

  async function avatarDataUrlFrom(row, avatarElement) {
    const documentRef = row.ownerDocument || document;
    if (!avatarElement || !documentRef || typeof documentRef.createElement !== "function") {
      return "";
    }

    await waitForImageReady(avatarElement);

    const width = avatarElement.naturalWidth || avatarElement.width;
    const height = avatarElement.naturalHeight || avatarElement.height;
    if (!width || !height) {
      return "";
    }

    try {
      const canvas = documentRef.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext && canvas.getContext("2d");
      if (!context || typeof context.drawImage !== "function") {
        return "";
      }
      context.drawImage(avatarElement, 0, 0, width, height);
      return canvas.toDataURL("image/png");
    } catch {
      return "";
    }
  }

  function extractComment(row) {
    if (!row || typeof row.querySelector !== "function") {
      return null;
    }

    const authorElement = row.querySelector("#author-name");
    const hoverMessageElement = row.querySelector("#hover-message");
    const messageElement = row.querySelector("#message");
    const authorName = normalizeText(authorElement && authorElement.textContent);
    const hoverMessage = normalizeText(hoverMessageElement && hoverMessageElement.textContent);
    const message = hoverMessage || normalizeText(messageElement && messageElement.textContent);

    if (!authorName || !message) {
      return null;
    }

    return {
      authorName,
      message,
      avatarUrl: avatarUrlFrom(row),
    };
  }

  async function extractCommentForPosting(row) {
    const comment = extractComment(row);
    if (!comment) {
      return null;
    }

    const avatarElement = avatarElementFrom(row);
    if (shouldInlineAvatar(row, avatarElement, comment.avatarUrl)) {
      const dataUrl = await avatarDataUrlFrom(row, avatarElement);
      if (dataUrl) {
        return {
          ...comment,
          avatarUrl: dataUrl,
        };
      }
    }

    return comment;
  }

  return {
    extractComment,
    extractCommentForPosting,
    normalizeText,
  };
});
