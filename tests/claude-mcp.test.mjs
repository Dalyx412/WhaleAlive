import test from 'node:test';
import assert from 'node:assert/strict';
import net from 'node:net';
import { once } from 'node:events';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const serverPath = fileURLToPath(new URL('../bridge/mcp-server.mjs', import.meta.url));
const helperPath = fileURLToPath(new URL('../tools/claude-config.mjs', import.meta.url));
let sequence = 0;
const pipe = () => `\\\\.\\pipe\\WhaleAlive.mcp-test.${process.pid}.${sequence++}`;
async function until(predicate) {
  const deadline = Date.now() + 4000;
  while (!predicate()) { assert.ok(Date.now() < deadline, 'MCP heartbeat timed out'); await delay(20); }
}

test('Claude config prints real absolute paths and the 12 async hooks', () => {
  const config = JSON.parse(execFileSync(process.execPath, [helperPath], {
    cwd: fileURLToPath(new URL('../tests', import.meta.url)), encoding: 'utf8', windowsHide: true,
  }));
  assert.equal(config.mcpServers.whale_alive.command, 'node');
  assert.deepEqual(config.mcpServers.whale_alive.args, [serverPath.replaceAll('\\', '/')]);
  assert.deepEqual(config.mcpServers.whale_alive.env, { WHALE_ALIVE_CLAUDE: '1' });
  const events = ['SessionStart', 'SessionEnd', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse',
    'PostToolUseFailure', 'PermissionRequest', 'Notification', 'SubagentStart', 'SubagentStop', 'Stop', 'StopFailure'];
  assert.deepEqual(Object.keys(config.hooks), events);
  const hookPath = fileURLToPath(new URL('../bridge/claude-hook.mjs', import.meta.url)).replaceAll('\\', '/');
  for (const [event, [group]] of Object.entries(config.hooks)) {
    assert.equal(group.matcher, ['UserPromptSubmit', 'Stop'].includes(event) ? undefined : '*');
    assert.deepEqual(group.hooks, [{ type: 'command', command: `node "${hookPath}"`, async: true, timeout: 5 }]);
  }
});

for (const enabled of [false, true]) {
  test(`MCP ${enabled ? 'Claude owns heartbeat until stdin closes' : 'default DSH entry has no Claude heartbeat'}`, async t => {
    const petPipe = pipe(), claudePipe = pipe(), messages = [];
    const sockets = new Set();
    const pet = net.createServer(socket => {
      sockets.add(socket); socket.on('close', () => sockets.delete(socket)); socket.on('error', () => {});
      let buffer = '';
      socket.on('data', chunk => {
        buffer += chunk;
        if (buffer.includes('\n')) {
          messages.push(JSON.parse(buffer.split('\n')[0]));
          socket.end('{"ok":true,"result":{}}\n');
        }
      });
    });
    pet.listen(petPipe); await once(pet, 'listening');
    t.after(() => { for (const s of sockets) s.destroy(); pet.close(); });
    const client = new Client({ name: 'whale-test', version: '1' });
    const env = { ...process.env, WHALE_ALIVE_PET_PIPE: petPipe, WHALE_ALIVE_CLAUDE_PIPE: claudePipe };
    delete env.WHALE_ALIVE_CLAUDE;
    if (enabled) env.WHALE_ALIVE_CLAUDE = '1';
    const transport = new StdioClientTransport({ command: process.execPath, args: [serverPath], env, stderr: 'pipe' });
    try {
      await client.connect(transport);
      assert.equal((await client.listTools()).tools.length, 15);
      const status = await client.callTool({ name: 'pet_status', arguments: {} });
      assert.equal(status.isError, false);
      if (enabled) {
        await until(() => messages.some(m => m.source === 'claude'));
        const socket = net.createConnection(claudePipe); await once(socket, 'connect');
        socket.end('{"hook_event_name":"UserPromptSubmit","session_id":"test"}\n');
        await until(() => messages.some(m => m.state === 'think'));
        await until(() => messages.filter(m => m.state === 'think').length >= 2);
      } else {
        await delay(2200);
        assert.deepEqual(messages, [{ action: 'status' }]);
      }
    } finally { await client.close(); }
    await delay(100); const count = messages.length;
    await delay(2100); assert.equal(messages.length, count, 'orphan heartbeat after MCP close');
  });
}
