(function startLiveCommentOverlayExtension() {
  const SERVER_URL = "http://127.0.0.1:3000";
  const decorator = window.LiveCommentDecorator;

  if (!decorator) {
    console.error("[Live Comment Overlay] Decorator script was not loaded.");
    return;
  }

  async function postJson(path, payload = {}) {
    const response = await fetch(`${SERVER_URL}${path}`, {
      method: "POST",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(`Overlay server responded with ${response.status}`);
    }

    return response.json();
  }

  function decorate(root = document.body) {
    return decorator.decorateTree(root, {
      postComment: (comment) => postJson("/api/comments/show", comment),
    });
  }

  function ensureClearControl() {
    if (document.querySelector(".lco-clear-control")) {
      return;
    }

    const control = document.createElement("div");
    control.className = "lco-clear-control";

    const button = document.createElement("button");
    button.type = "button";
    button.className = "lco-clear-button";
    button.textContent = "Clear";
    button.setAttribute("aria-label", "Clear OBS comment overlay");
    button.addEventListener("click", async () => {
      button.disabled = true;
      button.dataset.state = "sending";

      try {
        await postJson("/api/comments/clear");
        button.dataset.state = "sent";
      } catch (error) {
        button.dataset.state = "error";
        console.error("[Live Comment Overlay] Unable to clear comment", error);
      } finally {
        setTimeout(() => {
          button.dataset.state = "";
          button.disabled = false;
        }, 700);
      }
    });

    control.appendChild(button);
    document.body.appendChild(control);
  }

  function boot() {
    decorate();
    ensureClearControl();

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node.nodeType === Node.ELEMENT_NODE) {
            decorate(node);
          }
        }
      }
    });

    observer.observe(document.body, {
      childList: true,
      subtree: true,
    });

    window.LiveCommentOverlay = {
      decorate,
      observer,
      serverUrl: SERVER_URL,
    };
  }

  if (document.body) {
    boot();
  } else {
    window.addEventListener("DOMContentLoaded", boot, { once: true });
  }
})();
