const crypto = require("node:crypto");

class WebSocketHub {
  constructor() {
    this.clients = new Set();
  }

  handleUpgrade(request, socket) {
    const key = request.headers["sec-websocket-key"];
    if (!key) {
      socket.destroy();
      return null;
    }

    const accept = crypto
      .createHash("sha1")
      .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
      .digest("base64");

    socket.write(
      [
        "HTTP/1.1 101 Switching Protocols",
        "Upgrade: websocket",
        "Connection: Upgrade",
        `Sec-WebSocket-Accept: ${accept}`,
        "",
        "",
      ].join("\r\n"),
    );

    this.clients.add(socket);

    const cleanup = () => {
      this.clients.delete(socket);
    };

    socket.on("close", cleanup);
    socket.on("end", cleanup);
    socket.on("error", cleanup);
    socket.on("data", (buffer) => {
      this.handleClientFrame(socket, buffer);
    });

    return socket;
  }

  handleClientFrame(socket, buffer) {
    if (!buffer.length) {
      return;
    }

    const opcode = buffer[0] & 0x0f;
    if (opcode === 0x8) {
      this.clients.delete(socket);
      socket.end();
      return;
    }

    if (opcode === 0x9) {
      socket.write(encodeFrame(Buffer.alloc(0), 0x0a));
    }
  }

  send(socket, payload) {
    if (!this.clients.has(socket) || socket.destroyed) {
      return;
    }

    socket.write(encodeFrame(Buffer.from(JSON.stringify(payload), "utf8")));
  }

  broadcast(payload) {
    for (const client of this.clients) {
      this.send(client, payload);
    }
  }
}

function encodeFrame(payload, opcode = 0x1) {
  const length = payload.length;

  if (length < 126) {
    return Buffer.concat([Buffer.from([0x80 | opcode, length]), payload]);
  }

  if (length < 65536) {
    const header = Buffer.alloc(4);
    header[0] = 0x80 | opcode;
    header[1] = 126;
    header.writeUInt16BE(length, 2);
    return Buffer.concat([header, payload]);
  }

  const header = Buffer.alloc(10);
  header[0] = 0x80 | opcode;
  header[1] = 127;
  header.writeBigUInt64BE(BigInt(length), 2);
  return Buffer.concat([header, payload]);
}

module.exports = {
  WebSocketHub,
  encodeFrame,
};
