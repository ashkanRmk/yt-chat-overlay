(function initControlPage() {
  const VIDEO_ID_PATTERN = /^[A-Za-z0-9_-]{11}$/;

  const chatForm = document.querySelector("#chat-form");
  const chatInput = document.querySelector("#chat-input");
  const chatStatus = document.querySelector("#chat-status");
  const overlayInput = document.querySelector("#overlay-url");
  const copyOverlayButton = document.querySelector("#copy-overlay");
  const openOverlayButton = document.querySelector("#open-overlay");
  const clearOverlayButton = document.querySelector("#clear-overlay");
  const connectionStatus = document.querySelector("#connection-status");
  const connectionLabel = document.querySelector("#connection-label");
  const previewCard = document.querySelector("#preview-card");
  const previewAvatar = document.querySelector("#preview-avatar");
  const previewAuthor = document.querySelector("#preview-author");
  const previewMessage = document.querySelector("#preview-message");
  const manualForm = document.querySelector("#manual-form");
  const manualInput = document.querySelector("#manual-input");
  const manualStatus = document.querySelector("#manual-status");
  const fixtureItems = document.querySelector("#fixture-items");
  const addFixtureMessageButton = document.querySelector("#add-fixture-message");

  overlayInput.value = `${window.location.origin}/overlay`;

  chatForm.addEventListener("submit", (event) => {
    event.preventDefault();

    try {
      const url = toPopoutChatUrl(chatInput.value);
      window.open(url, "youtube-popout-chat", "popup=yes,width=460,height=900");
      setChatStatus("Chat opened in Chrome.", false);
    } catch (error) {
      setChatStatus(error.message, true);
    }
  });

  copyOverlayButton.addEventListener("click", async () => {
    await navigator.clipboard.writeText(overlayInput.value);
    flashButton(copyOverlayButton, "Copied");
  });

  openOverlayButton.addEventListener("click", () => {
    window.open(overlayInput.value, "live-comment-overlay", "width=1280,height=720");
  });

  clearOverlayButton.addEventListener("click", async () => {
    clearOverlayButton.disabled = true;
    try {
      await postJson("/api/comments/clear");
      flashButton(clearOverlayButton, "Cleared");
    } finally {
      clearOverlayButton.disabled = false;
    }
  });

  manualForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    const message = manualInput.value.trim();
    if (!message) {
      setManualStatus("Type a message first.", true);
      return;
    }

    const submitButton = manualForm.querySelector("button[type=submit]");
    submitButton.disabled = true;
    try {
      await postJson("/api/comments/show", { message, manual: true });
      setManualStatus("Shown on overlay.", false);
    } catch (error) {
      setManualStatus(error.message, true);
    } finally {
      submitButton.disabled = false;
    }
  });

  initFixturePanel();

  fetch("/api/comments/current")
    .then((response) => response.json())
    .then((payload) => renderPreview(payload.comment))
    .catch(() => setConnection("offline"));

  connectWebSocket();

  function extractVideoId(input) {
    const value = String(input || "").trim();
    if (VIDEO_ID_PATTERN.test(value)) {
      return value;
    }

    let url;
    try {
      url = new URL(value);
    } catch {
      throw new Error("Enter a valid YouTube video ID or chat URL.");
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

    if (!VIDEO_ID_PATTERN.test(videoId)) {
      throw new Error("Enter a valid YouTube video ID or chat URL.");
    }

    return videoId;
  }

  function toPopoutChatUrl(input) {
    const videoId = extractVideoId(input);
    return `https://studio.youtube.com/live_chat?is_popout=1&v=${encodeURIComponent(videoId)}`;
  }

  async function postJson(path, payload = {}) {
    const response = await fetch(path, {
      method: "POST",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(`Request failed with ${response.status}`);
    }

    return response.json();
  }

  function connectWebSocket() {
    const scheme = window.location.protocol === "https:" ? "wss" : "ws";
    const socket = new WebSocket(`${scheme}://${window.location.host}/ws`);

    socket.addEventListener("open", () => setConnection("online"));
    socket.addEventListener("message", (event) => {
      const payload = JSON.parse(event.data);
      if (payload.type === "init" || payload.type === "show") {
        renderPreview(payload.comment);
      }
      if (payload.type === "clear") {
        renderPreview(null);
      }
    });
    socket.addEventListener("close", () => {
      setConnection("offline");
      setTimeout(connectWebSocket, 1000);
    });
    socket.addEventListener("error", () => setConnection("offline"));
  }

  function initFixturePanel() {
    if (!fixtureItems || !window.LiveCommentDecorator) {
      return;
    }

    const decorateFixture = (root) =>
      window.LiveCommentDecorator.decorateTree(root, {
        postComment: (comment) => postJson("/api/comments/show", comment),
      });

    decorateFixture(fixtureItems);

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node.nodeType === Node.ELEMENT_NODE) {
            decorateFixture(node);
          }
        }
      }
    });
    observer.observe(fixtureItems, { childList: true });

    const samples = [
      ["@NewPersian", "پیام تازه از MutationObserver"],
      ["New English", "A newly appended chat row receives its Show button."],
      ["Emoji Test", "Hearts, stars, and faces ❤️ ⭐ 🙂"],
    ];
    let sampleIndex = 0;

    addFixtureMessageButton.addEventListener("click", () => {
      const sample = samples[sampleIndex % samples.length];
      sampleIndex += 1;

      const row = document.createElement("yt-live-chat-text-message-renderer");
      row.innerHTML = `
        <yt-img-shadow id="author-photo">
          <img alt="" src="/assets/fixture-avatar.svg">
        </yt-img-shadow>
        <div id="content">
          <span id="timestamp">now</span>
          <yt-live-chat-author-chip>
            <span id="author-name" dir="auto"></span>
          </yt-live-chat-author-chip>
          <div id="before-content-buttons"></div>
          <span id="message-container">
            <span id="message" dir="auto"></span>
          </span>
        </div>
      `;
      row.querySelector("#author-name").textContent = sample[0];
      row.querySelector("#message").textContent = sample[1];
      fixtureItems.appendChild(row);
    });
  }

  function renderPreview(comment) {
    previewCard.classList.toggle("is-empty", !comment);
    previewCard.classList.toggle("is-manual", Boolean(comment && comment.manual));
    previewAuthor.textContent = comment ? comment.authorName : "No comment selected";
    previewMessage.textContent = comment ? comment.message : "";
    previewAvatar.textContent = "";

    if (!comment) {
      previewAvatar.textContent = "-";
      return;
    }

    if (comment.manual) {
      previewAuthor.textContent = "Manual message";
      return;
    }

    if (comment.avatarUrl) {
      const image = document.createElement("img");
      image.alt = "";
      image.src = comment.avatarUrl;
      previewAvatar.appendChild(image);
    } else {
      previewAvatar.textContent = initialsFor(comment.authorName);
    }
  }

  function initialsFor(name) {
    return Array.from(String(name || "?").trim()).slice(0, 2).join("").toUpperCase();
  }

  function setConnection(state) {
    connectionStatus.classList.toggle("is-online", state === "online");
    connectionStatus.classList.toggle("is-offline", state === "offline");
    connectionLabel.textContent = state === "online" ? "Connected" : "Offline";
  }

  function setChatStatus(message, isError) {
    chatStatus.textContent = message;
    chatStatus.classList.toggle("is-error", isError);
  }

  function setManualStatus(message, isError) {
    manualStatus.textContent = message;
    manualStatus.classList.toggle("is-error", isError);
  }

  function flashButton(button, text) {
    const original = button.textContent;
    button.textContent = text;
    setTimeout(() => {
      button.textContent = original;
    }, 900);
  }
})();
