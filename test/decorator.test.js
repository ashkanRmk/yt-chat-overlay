const assert = require("node:assert/strict");
const { test } = require("node:test");

const { decorateRow, decorateTree } = require("../extension/decorator");

class FakeElement {
  constructor(tagName, selectors = {}) {
    this.tagName = tagName.toUpperCase();
    this.selectors = selectors;
    this.children = [];
    this.dataset = {};
    this.attributes = {};
    this.listeners = {};
    this.textContent = "";
    this.disabled = false;
    this.ownerDocument = fakeDocument;
  }

  querySelector(selector) {
    return this.selectors[selector] || null;
  }

  querySelectorAll(selector) {
    if (selector !== "yt-live-chat-text-message-renderer") {
      return [];
    }
    return this.children.filter((child) => child.matches(selector));
  }

  matches(selector) {
    return selector === "yt-live-chat-text-message-renderer" && this.tagName === selector.toUpperCase();
  }

  appendChild(child) {
    this.children.push(child);
    child.parentNode = this;
    return child;
  }

  setAttribute(name, value) {
    this.attributes[name] = value;
  }

  addEventListener(type, handler) {
    this.listeners[type] = handler;
  }

  async click() {
    await this.listeners.click({
      preventDefault() {},
      stopPropagation() {},
    });
  }
}

const fakeDocument = {
  createElement(tagName) {
    return new FakeElement(tagName);
  },
};

function chatRow() {
  const host = new FakeElement("div");
  const avatar = { currentSrc: "https://yt.example/sara.jpg" };
  const row = new FakeElement("yt-live-chat-text-message-renderer", {
    "#author-name": { textContent: "Sara" },
    "#message": { textContent: "سلام from chat" },
    "#author-photo img": avatar,
    "#before-content-buttons": host,
  });
  return { avatar, host, row };
}

test("decorateRow injects one Show button and posts extracted comment on click", async () => {
  const { host, row } = chatRow();
  const posted = [];

  assert.equal(decorateRow(row, { postComment: (comment) => posted.push(comment) }), true);
  assert.equal(row.dataset.lcoDecorated, "true");
  assert.equal(host.children.length, 1);
  assert.equal(host.children[0].textContent, "Show");

  await host.children[0].click();

  assert.deepEqual(posted, [
    {
      authorName: "Sara",
      message: "سلام from chat",
      avatarUrl: "https://yt.example/sara.jpg",
    },
  ]);
});

test("decorateRow does not duplicate buttons on the same chat row", () => {
  const { host, row } = chatRow();

  decorateRow(row, { postComment: () => {} });
  decorateRow(row, { postComment: () => {} });

  assert.equal(host.children.length, 1);
});

test("decorateRow extracts the avatar at click time so late-loaded new comments keep profile pictures", async () => {
  const { avatar, host, row } = chatRow();
  const posted = [];
  avatar.currentSrc = "";

  assert.equal(decorateRow(row, { postComment: (comment) => posted.push(comment) }), true);

  avatar.currentSrc = "https://yt.example/late-loaded.jpg";
  await host.children[0].click();

  assert.equal(posted[0].avatarUrl, "https://yt.example/late-loaded.jpg");
});

test("decorateRow uses posting extraction when available", async () => {
  const { host, row } = chatRow();
  const posted = [];

  assert.equal(
    decorateRow(row, {
      extractComment: () => ({ authorName: "Sara", message: "سلام", avatarUrl: "" }),
      extractCommentForPosting: async () => ({
        authorName: "Sara",
        message: "سلام",
        avatarUrl: "data:image/png;base64,aGVsbG8=",
      }),
      postComment: (comment) => posted.push(comment),
    }),
    true,
  );

  await host.children[0].click();

  assert.equal(posted[0].avatarUrl, "data:image/png;base64,aGVsbG8=");
});

test("decorateTree decorates existing child chat rows", () => {
  const root = new FakeElement("div");
  root.appendChild(chatRow().row);
  root.appendChild(chatRow().row);

  assert.equal(decorateTree(root, { postComment: () => {} }), 2);
});
