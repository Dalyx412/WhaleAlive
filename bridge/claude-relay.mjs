import net from 'node:net';
import { request } from './pet.mjs';
import { createClaudeTracker } from './claude-state.mjs';

export function startClaudeRelay({
  petRequest = request,
  pipeName = process.env.WHALE_ALIVE_CLAUDE_PIPE || '\\\\.\\pipe\\WhaleAlive.Claude',
  now = Date.now,
  intervalMs = 2000,
} = {}) {
  let stopped = false, owner = false, inFlight = false;
  let server, retry, heartbeat, lastSent;
  let tracker = createClaudeTracker(now);
  const sockets = new Set();

  async function sync(force = false) {
    if (stopped || !owner || inFlight) return;
    const state = tracker.state();
    if (!force && state === lastSent) return;
    inFlight = true;
    lastSent = state;
    try { await petRequest({ action: 'state', state, source: 'claude' }, 1000); }
    catch { /* The pet can be absent or restarting. Retry on the next heartbeat. */ }
    finally {
      inFlight = false;
      // A state change during a slow request must not wait for the next heartbeat.
      if (!stopped && owner && tracker.state() !== lastSent) void sync();
    }
  }

  function connection(socket) {
    sockets.add(socket);
    socket.unref();
    socket.on('error', () => {});
    socket.once('close', () => sockets.delete(socket));
    // Absolute deadline also bounds clients that never finish a line.
    const deadline = setTimeout(() => socket.destroy(), 1000);
    deadline.unref();
    socket.once('close', () => clearTimeout(deadline));
    const chunks = [];
    let size = 0, done = false;
    socket.on('data', chunk => {
      if (done) return;
      const end = chunk.indexOf(10);
      const part = end < 0 ? chunk : chunk.subarray(0, end + 1);
      size += part.length;
      if (size > 8192) { done = true; socket.destroy(); return; }
      chunks.push(part);
      if (end < 0) return;
      done = true;
      try { tracker.event(JSON.parse(Buffer.concat(chunks, size).toString('utf8'))); }
      catch { /* Malformed events are observational no-ops. */ }
      socket.end();
      void sync();
      // Stop/StopFailure bursts last 2500 ms; flush their expiry instead of waiting for a heartbeat.
      setTimeout(() => void sync(), 2600).unref();
    });
  }

  function listen() {
    if (stopped) return;
    const candidate = net.createServer(connection);
    server = candidate;
    candidate.on('error', () => {
      owner = false;
      clearInterval(heartbeat);
      candidate.close();
      for (const socket of sockets) socket.destroy();
      if (!stopped) { retry = setTimeout(listen, 2000); retry.unref(); }
    });
    try {
      candidate.listen(pipeName, () => {
        if (stopped) { candidate.close(); return; }
        owner = true;
        tracker = createClaudeTracker(now);
        lastSent = undefined;
        heartbeat = setInterval(() => void sync(true), intervalMs);
        heartbeat.unref();
        void sync(true);
      });
      candidate.unref();
    } catch {
      candidate.close();
      retry = setTimeout(listen, 2000); retry.unref();
    }
  }
  listen();
  return {
    stop() {
      stopped = true; owner = false;
      clearTimeout(retry); clearInterval(heartbeat);
      for (const socket of sockets) socket.destroy();
      server?.close();
    },
  };
}
