import test from 'node:test';
import assert from 'node:assert/strict';
import net from 'node:net';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { startClaudeRelay } from '../bridge/claude-relay.mjs';
import { request } from '../bridge/pet.mjs';

let sequence = 0;
const pipe = () => `\\\\.\\pipe\\WhaleAlive.relay-test.${process.pid}.${sequence++}`;
async function until(predicate, timeout = 4000) {
  const end = Date.now() + timeout;
  while (!predicate()) {
    assert.ok(Date.now() < end, 'timed out waiting for relay');
    await delay(10);
  }
}
async function send(name, event) {
  const socket = net.createConnection(name);
  const closed = once(socket, 'close');
  await once(socket, 'connect');
  socket.end(typeof event === 'string' ? event : JSON.stringify({ session_id: 'a', ...event }) + '\n');
  await closed;
}
async function fakePet(t) {
  const name = pipe(), messages = [], sockets = new Set();
  const previous = process.env.WHALE_ALIVE_PET_PIPE;
  process.env.WHALE_ALIVE_PET_PIPE = name;
  const server = net.createServer(socket => {
    sockets.add(socket); socket.on('close', () => sockets.delete(socket)); socket.on('error', () => {});
    let buffer = '';
    socket.on('data', chunk => {
      buffer += chunk;
      if (!buffer.includes('\n')) return;
      messages.push({ command: JSON.parse(buffer.split('\n')[0]), at: Date.now() });
      socket.end('{"ok":true}\n');
    });
  });
  server.listen(name); await once(server, 'listening');
  t.after(() => {
    for (const socket of sockets) socket.destroy();
    server.close();
    if (previous === undefined) delete process.env.WHALE_ALIVE_PET_PIPE;
    else process.env.WHALE_ALIVE_PET_PIPE = previous;
  });
  return messages;
}

test('Windows named pipe rejects a second listener with EADDRINUSE', async t => {
  const name = pipe(), first = net.createServer(), second = net.createServer();
  t.after(() => { first.close(); second.close(); });
  first.listen(name); await once(first, 'listening');
  const error = once(second, 'error'); second.listen(name);
  const [result] = await error;
  assert.equal(result.code, 'EADDRINUSE');
  t.diagnostic(`second listen on ${process.platform}: ${result.code}`);
});

test('Claude relay sends changed state immediately and idle heartbeats through the pet pipe', async t => {
  const messages = await fakePet(t), name = pipe();
  let time = 0;
  const relay = startClaudeRelay({ pipeName: name, intervalMs: 60, now: () => time });
  t.after(() => relay.stop());
  await until(() => messages.length >= 3);
  assert.ok(messages.slice(0, 3).every(m => m.command.state === 'idle'));
  for (let i = 1; i < 3; i++) assert.ok(messages[i].at - messages[i - 1].at >= 30);
  await send(name, { hook_event_name: 'UserPromptSubmit' });
  await until(() => messages.at(-1).command.state === 'think');
  await send(name, { hook_event_name: 'PreToolUse', tool_use_id: '1' });
  await until(() => messages.at(-1).command.state === 'working');
  await send(name, { hook_event_name: 'PermissionRequest' });
  await until(() => messages.at(-1).command.state === 'wait');
  await send(name, { hook_event_name: 'Stop' });
  await until(() => messages.at(-1).command.state === 'celebrate');
  time = 2500;
  await until(() => messages.at(-1).command.state === 'idle');
  assert.ok(messages.every(m => m.command.source === 'claude' && m.command.action === 'state'));
  relay.stop(); await delay(30); const count = messages.length;
  await delay(140); assert.equal(messages.length, count);
});

test('Claude relay permits one owner and the standby takes over after owner stops', async t => {
  const messages = await fakePet(t), name = pipe(), owners = [];
  const sender = id => async (command, timeout) => {
    owners.push(id); assert.equal(timeout, 1000); return request(command, timeout);
  };
  const first = startClaudeRelay({ pipeName: name, intervalMs: 50, petRequest: sender('first') });
  t.after(() => first.stop());
  await until(() => messages.length > 0);
  const second = startClaudeRelay({ pipeName: name, intervalMs: 50, petRequest: sender('second') });
  t.after(() => second.stop());
  await delay(2150); assert.ok(owners.every(id => id === 'first'));
  first.stop();
  await until(() => owners.includes('second'));
  const boundary = owners.indexOf('second');
  await send(name, { hook_event_name: 'UserPromptSubmit' });
  await until(() => messages.at(-1).command.state === 'think');
  assert.ok(owners.slice(boundary).every(id => id === 'second'));
});

test('Claude relay bounds framing, ignores garbage and consumes only the first line', async t => {
  const messages = await fakePet(t), name = pipe();
  const relay = startClaudeRelay({ pipeName: name, intervalMs: 50 }); t.after(() => relay.stop());
  await until(() => messages.length > 0);
  await send(name, '{bad}\n');
  await send(name, 'x'.repeat(8193) + '\n');
  await send(name, '{"hook_event_name":"PermissionRequest","session_id":"a"}');
  await delay(80); assert.ok(messages.every(m => m.command.state === 'idle'));
  await send(name, '{"hook_event_name":"UserPromptSubmit","session_id":"a"}\n{"hook_event_name":"PermissionRequest","session_id":"a"}\n');
  await until(() => messages.at(-1).command.state === 'think');
  assert.ok(messages.every(m => m.command.state !== 'wait'));
});

test('Claude relay serializes requests, catches failures and promptly flushes intervening changes', async t => {
  const name = pipe(), sent = [];
  let release, active = 0, maximum = 0;
  const relay = startClaudeRelay({ pipeName: name, intervalMs: 30, petRequest: async command => {
    active++; maximum = Math.max(maximum, active); sent.push(command.state);
    try {
      if (sent.length === 1) await new Promise(resolve => { release = resolve; });
      else throw new Error('pet absent');
    } finally { active--; }
  } });
  t.after(() => { release?.(); relay.stop(); });
  await until(() => release);
  await send(name, { hook_event_name: 'UserPromptSubmit' });
  await send(name, { hook_event_name: 'PermissionRequest' });
  await delay(80); assert.deepEqual(sent, ['idle']);
  release(); await until(() => sent.includes('wait'));
  await until(() => sent.length >= 3); assert.equal(maximum, 1);
});
