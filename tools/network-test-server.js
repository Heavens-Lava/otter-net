// A local server for tests/network: programs that need a peer in another
// process (a synchronous `get` blocks the program that would answer it, and
// Otter has no WebSocket server of its own).
//
//   node tools/network-test-server.js      HTTP and WebSocket on 127.0.0.1:18765
//   pwsh -NoProfile -Command "& tools/compare.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll -NativeOnly -IncludeEventPrograms -Corpus @('tests/network')"
//
// HTTP: GET /hello.txt and GET /data.json; any other path is 404; any other
// method 501. WebSocket (any path): text comes back as "echo: <text>", "bye"
// makes the server close with 4001 "done", binary comes back as it is, and a
// close is answered with the same close.
const http = require('http');
const crypto = require('crypto');

const files = {
    '/hello.txt': ['text/plain', 'hello there\n'],
    '/data.json': ['application/json', '{"name": "Ann", "n": [1, 2.5]}\n'],
};

const server = http.createServer((request, response) => {
    if (request.method !== 'GET') { response.writeHead(501, { 'Content-Type': 'text/plain' }); response.end('not implemented\n'); return; }
    const file = files[request.url];
    if (!file) { response.writeHead(404, { 'Content-Type': 'text/plain' }); response.end('missing\n'); return; }
    response.writeHead(200, { 'Content-Type': file[0] });
    response.end(file[1]);
});

server.on('upgrade', (request, socket) => {
    const accept = crypto.createHash('sha1').update(request.headers['sec-websocket-key'] + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
    let head = 'HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n';
    if (request.headers['sec-websocket-protocol']) head += 'Sec-WebSocket-Protocol: ' + request.headers['sec-websocket-protocol'].split(',')[0].trim() + '\r\n';
    socket.write(head + '\r\n');
    let buffer = Buffer.alloc(0);
    const send = (op, payload) => {
        let header;
        if (payload.length < 126) header = Buffer.from([0x80 | op, payload.length]);
        else { header = Buffer.alloc(4); header[0] = 0x80 | op; header[1] = 126; header.writeUInt16BE(payload.length, 2); }
        socket.write(Buffer.concat([header, payload]));
    };
    socket.on('data', data => {
        buffer = Buffer.concat([buffer, data]);
        while (buffer.length >= 2) {
            const op = buffer[0] & 15;
            let length = buffer[1] & 127;
            let offset = 2;
            if (length === 126) { length = buffer.readUInt16BE(2); offset = 4; }
            const mask = buffer.slice(offset, offset + 4);
            offset += 4;
            if (buffer.length < offset + length) break;
            const payload = Buffer.from(buffer.slice(offset, offset + length));
            for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
            buffer = buffer.slice(offset + length);
            if (op === 1) {
                const text = payload.toString();
                if (text === 'bye') {
                    const close = Buffer.alloc(6);
                    close.writeUInt16BE(4001, 0);
                    close.write('done', 2);
                    send(8, close);
                }
                else send(1, Buffer.from('echo: ' + text));
            }
            else if (op === 2) send(2, payload);
            else if (op === 8) { send(8, payload); socket.end(); }
            else if (op === 9) send(10, payload);
        }
    });
    socket.on('error', () => { });
});

server.listen(18765, '127.0.0.1');
