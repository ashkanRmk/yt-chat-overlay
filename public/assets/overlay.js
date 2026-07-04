(function initOverlay() {
  const card = document.querySelector("#comment-card");
  const avatarShell = document.querySelector(".avatar-shell");
  const avatarImage = document.querySelector("#avatar-image");
  const avatarFallback = document.querySelector("#avatar-fallback");
  const authorName = document.querySelector("#author-name");
  const messageText = document.querySelector("#message-text");

  const FADE_OUT_MS = 220;
  let visible = false;
  let transitionTimer = null;
  let reconnectTimer = null;

  fetch("/api/comments/current")
    .then((response) => response.json())
    .then((payload) => {
      if (payload.comment) {
        showComment(payload.comment);
      }
    })
    .catch(() => {});

  connectWebSocket();

  function connectWebSocket() {
    const scheme = window.location.protocol === "https:" ? "wss" : "ws";
    const socket = new WebSocket(`${scheme}://${window.location.host}/ws`);

    socket.addEventListener("message", (event) => {
      const payload = JSON.parse(event.data);
      if (payload.type === "init" && payload.comment) {
        showComment(payload.comment);
      }
      if (payload.type === "show") {
        showComment(payload.comment);
      }
      if (payload.type === "clear") {
        clearComment();
      }
    });

    socket.addEventListener("close", scheduleReconnect);
    socket.addEventListener("error", scheduleReconnect);
  }

  function scheduleReconnect() {
    if (reconnectTimer) {
      return;
    }

    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connectWebSocket();
    }, 1000);
  }

  function showComment(comment) {
    if (!comment) {
      clearComment();
      return;
    }

    if (visible || transitionTimer) {
      transitionToComment(comment);
      return;
    }

    renderComment(comment);
    card.setAttribute("aria-hidden", "false");
    requestAnimationFrame(() => {
      card.classList.add("is-visible");
      visible = true;
    });
  }

  function clearComment() {
    if (transitionTimer) {
      clearTimeout(transitionTimer);
      transitionTimer = null;
    }
    card.classList.remove("is-visible", "is-swapping");
    card.setAttribute("aria-hidden", "true");
    visible = false;
  }

  function transitionToComment(comment) {
    if (transitionTimer) {
      clearTimeout(transitionTimer);
    }

    card.classList.remove("is-visible");
    transitionTimer = window.setTimeout(() => {
      renderComment(comment);
      card.setAttribute("aria-hidden", "false");
      requestAnimationFrame(() => {
        card.classList.add("is-visible");
        visible = true;
        transitionTimer = null;
      });
    }, FADE_OUT_MS);
  }

  function renderComment(comment) {
    card.classList.toggle("is-manual", comment.manual === true);
    authorName.textContent = displayAuthorName(comment.authorName);
    messageText.textContent = comment.message;
    authorName.setAttribute("dir", "rtl");
    messageText.setAttribute("dir", "auto");

    avatarShell.classList.toggle("has-image", Boolean(comment.avatarUrl));
    avatarImage.removeAttribute("src");
    avatarFallback.textContent = initialsFor(comment.authorName);

    if (comment.avatarUrl) {
      avatarImage.src = comment.avatarUrl;
    }
  }

  function displayAuthorName(name) {
    const value = String(name || "").trim();
    if (!value.startsWith("@") || value.length === 1) {
      return value;
    }

    const withoutAt = value.slice(1).trim();
    return `${withoutAt}@`;
  }

  function initialsFor(name) {
    const chars = Array.from(String(name || "?").trim()).filter((char) => char !== "@");
    return chars.slice(0, 2).join("").toUpperCase() || "?";
  }
})();
