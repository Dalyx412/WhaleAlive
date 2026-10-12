import net from 'node:net';

// Observation only: no stdout, replies, retries, or payloads outside this allowlist.
const exit = () => process.exit(0);
setTimeout(exit, 300);
process.on('uncaughtException', exit);
process.stdin.on('error', exit);
const chunks = [];
let size = 0;
process.stdin.on('data', chunk => {
  size += chunk.length;
  if (size > 1024 * 1024) return exit();
  chunks.push(chunk);
});
process.stdin.on('end', () => {
  try {
    const input = JSON.parse(Buffer.concat(chunks, size).toString('utf8'));
    if (!input || typeof input !== 'object' || Array.isArray(input)) return exit();
    const event = {};
    for (const key of ['hook_event_name', 'session_id', 'tool_name', 'tool_use_id', 'agent_id', 'notification_type']) {
      if (typeof input[key] === 'string') event[key] = input[key];
    }
    if (event.hook_event_name === 'SessionStart' && typeof input.source === 'string') event.source = input.source;
    if (event.hook_event_name === 'SessionEnd' && typeof input.reason === 'string') event.reason = input.reason;
    const line = JSON.stringify(event) + '\n';
    if (Buffer.byteLength(line) > 8192) return exit();
    const socket = net.createConnection(process.env.WHALE_ALIVE_CLAUDE_PIPE || '\\\\.\\pipe\\WhaleAlive.Claude');
    socket.on('error', exit);
    socket.once('connect', () => socket.end(line, exit));
  } catch { exit(); }
});
