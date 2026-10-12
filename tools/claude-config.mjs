import { fileURLToPath } from 'node:url';

const path = file => fileURLToPath(new URL(file, import.meta.url)).replaceAll('\\', '/');
const hook = { type: 'command', command: `node "${path('../bridge/claude-hook.mjs')}"`, async: true, timeout: 5 };
const events = ['SessionStart', 'SessionEnd', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse',
  'PostToolUseFailure', 'PermissionRequest', 'Notification', 'SubagentStart', 'SubagentStop', 'Stop', 'StopFailure'];
const noMatcher = new Set(['UserPromptSubmit', 'Stop']);
console.log(JSON.stringify({
  mcpServers: { whale_alive: {
    command: 'node', args: [path('../bridge/mcp-server.mjs')], env: { WHALE_ALIVE_CLAUDE: '1' },
  } },
  hooks: Object.fromEntries(events.map(event => [event, [{
    ...(noMatcher.has(event) ? {} : { matcher: '*' }), hooks: [hook],
  }]])),
}, null, 2));
