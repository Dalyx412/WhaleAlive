import test from 'node:test';
import assert from 'node:assert/strict';
import net from 'node:net';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { performance } from 'node:perf_hooks';
import { once } from 'node:events';

const hook = fileURLToPath(new URL('../bridge/claude-hook.mjs', import.meta.url));
let sequence = 0;
const pipe = () => `\\\\.\\pipe\\WhaleAlive.hook-test.${process.pid}.${sequence++}`;
async function invoke(t, name, input, keepOpen = false) {
  const start = performance.now();
  const child = spawn(process.execPath, [hook], { env: { ...process.env, WHALE_ALIVE_CLAUDE_PIPE: name }, windowsHide: true });
  let stdout = '', stderr = '';
  child.stdout.on('data', c => { stdout += c; }); child.stderr.on('data', c => { stderr += c; });
  child.stdin.on('error', () => {});
  const closed = once(child, 'close');
  if (keepOpen) child.stdin.write(input); else child.stdin.end(input);
  const deadline = setTimeout(() => child.kill(), 2000);
  const [code, signal] = await closed;
  clearTimeout(deadline);
  const elapsed = performance.now() - start;
  t.diagnostic(`hook wall time: ${elapsed.toFixed(1)} ms`);
  assert.equal(code, 0); assert.equal(signal, null);
  assert.equal(stdout, ''); assert.equal(stderr, '');
  // Includes OS process startup; the internal lifetime watchdog is 300 ms.
  assert.ok(elapsed < 1000, `hook exceeded startup allowance: ${elapsed} ms`);
}

test('Claude hook forwards only allowlisted strings, with lifecycle-specific source/reason', async t => {
  const name = pipe(); const received = [];
  const server = net.createServer(socket => {
    let line = '';
    socket.on('error', () => {});
    socket.on('data', chunk => { line += chunk; if (line.includes('\n')) received.push(JSON.parse(line)); });
  });
  server.listen(name); await once(server, 'listening');
  t.after(() => server.close());
  const safe = { hook_event_name: 'PreToolUse', session_id: 'a', tool_name: 'Read', tool_use_id: 't', agent_id: 'child', notification_type: 'permission_prompt' };
  const privateFields = { prompt: 'secret', tool_input: { path: 'secret' }, tool_response: 'secret', message: 'secret', transcript_path: 'secret', cwd: 'secret', source: 'startup', reason: 'logout' };
  await invoke(t, name, JSON.stringify({ ...safe, ...privateFields }));
  assert.deepEqual(received.shift(), safe);
  await invoke(t, name, JSON.stringify({ ...safe, ...privateFields, hook_event_name: 'SessionStart' }));
  assert.deepEqual(received.shift(), { ...safe, hook_event_name: 'SessionStart', source: 'startup' });
  await invoke(t, name, JSON.stringify({ ...safe, ...privateFields, hook_event_name: 'SessionEnd' }));
  assert.deepEqual(received.shift(), { ...safe, hook_event_name: 'SessionEnd', reason: 'logout' });
  await invoke(t, name, JSON.stringify({ ...safe, tool_name: { prompt: 'secret' } }));
  const { tool_name, ...withoutName } = safe; assert.deepEqual(received.shift(), withoutName);
});

test('Claude hook silently exits 0 quickly without a listening server', async t => {
  await invoke(t, pipe(), JSON.stringify({ hook_event_name: 'Stop', session_id: 'a' }));
});
test('Claude hook silently exits 0 on malformed or oversized stdin', async t => {
  await invoke(t, pipe(), '{malformed');
  await invoke(t, pipe(), 'x'.repeat(1024 * 1024 + 1));
});
test('Claude hook lifetime is bounded even when stdin never closes', async t => {
  await invoke(t, pipe(), '{', true);
});
