(function attachDecorator(root, factory) {
  const extractor =
    root.LiveCommentExtractor ||
    (typeof require === "function" ? require("./comment-extractor") : null);
  const api = factory(extractor);

  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }

  root.LiveCommentDecorator = api;
})(typeof globalThis !== "undefined" ? globalThis : window, function createDecorator(extractor) {
  const CHAT_ROW_SELECTOR = "yt-live-chat-text-message-renderer";

  function createShowButton(documentRef, onClick) {
    const button = documentRef.createElement("button");
    button.type = "button";
    button.className = "lco-show-button";
    button.textContent = "Show";
    button.setAttribute("aria-label", "Show this chat message on the OBS overlay");
    button.setAttribute("title", "Show on overlay");

    button.addEventListener("click", async (event) => {
      event.preventDefault();
      event.stopPropagation();

      if (button.disabled) {
        return;
      }

      const originalText = button.textContent;
      button.disabled = true;
      button.dataset.state = "sending";
      button.textContent = "Showing";

      try {
        await onClick();
        button.dataset.state = "sent";
        button.textContent = "Shown";
        setTimeout(() => {
          button.dataset.state = "";
          button.textContent = originalText;
        }, 900);
      } catch (error) {
        button.dataset.state = "error";
        button.textContent = "Retry";
        console.error("[Live Comment Overlay] Unable to show comment", error);
      } finally {
        button.disabled = false;
      }
    });

    return button;
  }

  function decorateRow(row, options = {}) {
    if (!row || row.dataset.lcoDecorated === "true") {
      return false;
    }

    const extract = options.extractComment || extractor.extractComment;
    const extractForPosting =
      options.extractCommentForPosting || extractor.extractCommentForPosting || extract;
    if (!extract(row)) {
      return false;
    }

    const documentRef = options.document || row.ownerDocument || document;
    const postComment = options.postComment || (() => Promise.resolve());
    const button = createShowButton(documentRef, async () => {
      const latestComment = await extractForPosting(row);
      if (!latestComment) {
        throw new Error("Unable to extract this chat row.");
      }
      return postComment(latestComment);
    });
    const host = row.querySelector("#before-content-buttons") || row.querySelector("#content") || row;

    host.appendChild(button);
    row.dataset.lcoDecorated = "true";

    return true;
  }

  function decorateTree(root, options = {}) {
    if (!root) {
      return 0;
    }

    let count = 0;

    if (typeof root.matches === "function" && root.matches(CHAT_ROW_SELECTOR)) {
      count += decorateRow(root, options) ? 1 : 0;
    }

    if (typeof root.querySelectorAll === "function") {
      for (const row of root.querySelectorAll(CHAT_ROW_SELECTOR)) {
        count += decorateRow(row, options) ? 1 : 0;
      }
    }

    return count;
  }

  return {
    CHAT_ROW_SELECTOR,
    createShowButton,
    decorateRow,
    decorateTree,
  };
});
