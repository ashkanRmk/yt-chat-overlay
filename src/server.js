const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const { sanitizeComment } = require("./comment-sanitizer");
const { WebSocketHub } = require("./websocket-hub");

const PROJECT_ROOT = path.resolve(__dirname, "..");
const PUBLIC_DIR = path.join(PROJECT_ROOT, "public");
const EXTENSION_DIR = path.join(PROJECT_ROOT, "extension");

function createOverlayServer() {
  let currentComment = null;
  const hub = new WebSocketHub();

  const server = http.createServer(async (request, response) => {
    addCorsHeaders(response);

    if (request.method === "OPTIONS") {
      response.writeHead(204);
      response.end();
      return;
    }

    const url = new URL(request.url, "http://127.0.0.1");

    try {
      if (request.method === "GET" && url.pathname === "/api/comments/current") {
        sendJson(response, 200, { comment: currentComment });
        return;
      }

      if (request.method === "POST" && url.pathname === "/api/comments/show") {
        const body = await readJsonBody(request);
        currentComment = sanitizeComment(body);
        hub.broadcast({ type: "show", comment: currentComment });
        sendJson(response, 200, { ok: true, comment: currentComment });
        return;
      }

      if (request.method === "POST" && url.pathname === "/api/comments/clear") {
        currentComment = null;
        hub.broadcast({ type: "clear" });
        sendJson(response, 200, { ok: true, comment: null });
        return;
      }

      if (request.method !== "GET") {
        sendJson(response, 405, { error: "Method not allowed." });
        return;
      }

      if (url.pathname === "/") {
        serveFile(response, path.join(PUBLIC_DIR, "index.html"));
        return;
      }

      if (url.pathname === "/overlay") {
        serveFile(response, path.join(PUBLIC_DIR, "overlay.html"));
        return;
      }

      if (url.pathname.startsWith("/assets/")) {
        serveFromRoot(response, path.join(PUBLIC_DIR, "assets"), url.pathname.replace(/^\/assets\//, ""));
        return;
      }

      if (url.pathname.startsWith("/extension/")) {
        serveFromRoot(response, EXTENSION_DIR, url.pathname.replace(/^\/extension\//, ""));
        return;
      }

      sendJson(response, 404, { error: "Not found." });
    } catch (error) {
      const statusCode = error.statusCode || 500;
      sendJson(response, statusCode, { error: error.message || "Internal server error." });
    }
  });

  server.on("upgrade", (request, socket) => {
    const url = new URL(request.url, "http://127.0.0.1");
    if (url.pathname !== "/ws") {
      socket.destroy();
      return;
    }

    const client = hub.handleUpgrade(request, socket);
    if (client) {
      hub.send(client, { type: "init", comment: currentComment });
    }
  });

  return {
    server,
    getCurrentComment: () => currentComment,
  };
}

function addCorsHeaders(response) {
  response.setHeader("Access-Control-Allow-Origin", "*");
  response.setHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  response.setHeader("Access-Control-Allow-Headers", "content-type");
  response.setHeader("Cross-Origin-Resource-Policy", "cross-origin");
}

async function readJsonBody(request) {
  const chunks = [];
  let size = 0;

  for await (const chunk of request) {
    size += chunk.length;
    if (size > 64 * 1024) {
      const error = new Error("Request body is too large.");
      error.statusCode = 413;
      throw error;
    }
    chunks.push(chunk);
  }

  const raw = Buffer.concat(chunks).toString("utf8");
  if (!raw) {
    return {};
  }

  try {
    return JSON.parse(raw);
  } catch {
    const error = new Error("Request body must be valid JSON.");
    error.statusCode = 400;
    throw error;
  }
}

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8",
    "cache-control": "no-store",
  });
  response.end(JSON.stringify(payload));
}

function serveFromRoot(response, root, relativePath) {
  let decodedPath;
  try {
    decodedPath = decodeURIComponent(relativePath);
  } catch {
    sendJson(response, 400, { error: "Invalid path." });
    return;
  }

  const filePath = path.resolve(root, decodedPath);
  if (!filePath.startsWith(`${root}${path.sep}`)) {
    sendJson(response, 403, { error: "Forbidden." });
    return;
  }

  serveFile(response, filePath);
}

function serveFile(response, filePath) {
  fs.readFile(filePath, (error, data) => {
    if (error) {
      sendJson(response, error.code === "ENOENT" ? 404 : 500, {
        error: error.code === "ENOENT" ? "Not found." : "Unable to read file.",
      });
      return;
    }

    response.writeHead(200, {
      "content-type": contentTypeFor(filePath),
      "cache-control": "no-store",
    });
    response.end(data);
  });
}

function contentTypeFor(filePath) {
  const extension = path.extname(filePath);
  switch (extension) {
    case ".css":
      return "text/css; charset=utf-8";
    case ".html":
      return "text/html; charset=utf-8";
    case ".js":
      return "text/javascript; charset=utf-8";
    case ".json":
      return "application/json; charset=utf-8";
    case ".svg":
      return "image/svg+xml";
    case ".woff2":
      return "font/woff2";
    default:
      return "application/octet-stream";
  }
}

if (require.main === module) {
  const port = Number(process.env.PORT || 3000);
  const host = "127.0.0.1";
  const { server } = createOverlayServer();

  server.listen(port, host, () => {
    const baseUrl = `http://${host}:${port}`;
    console.log(`Control page: ${baseUrl}/`);
    console.log(`OBS overlay:  ${baseUrl}/overlay`);
  });
}

module.exports = {
  createOverlayServer,
};
