import test from 'node:test';
import assert from 'node:assert/strict';
import { createClaudeTracker } from '../bridge/claude-state.mjs';

function setup() {
  let time = 0;
  const tracker = createClaudeTracker(() => time);
  return { tracker, advance: ms => { time += ms; },
    event: (hook_event_name, fields = {}) => tracker.event({ hook_event_name, session_id: 'a', ...fields }),
    state: () => tracker.state() };
}

test('Claude thinking, parallel tools and failure return to thinking', () => {
  const t = setup();
  t.event('SessionStart'); assert.equal(t.state(), 'idle');
  t.event('UserPromptSubmit'); assert.equal(t.state(), 'think');
  t.event('PreToolUse', { tool_use_id: '1' }); assert.equal(t.state(), 'working');
  t.event('PreToolUse', { tool_use_id: '2' });
  t.event('PostToolUse', { tool_use_id: '1' }); assert.equal(t.state(), 'working');
  t.event('PostToolUseFailure', { tool_use_id: '2' }); assert.equal(t.state(), 'think');
});

test('Claude permission waits have priority and tool completion clears wait', () => {
  const t = setup();
  t.event('StopFailure', { session_id: 'failed' });
  for (const e of [
    ['PermissionRequest', {}],
    ['PreToolUse', { tool_use_id: 'ask', tool_name: 'AskUserQuestion' }],
    ['PreToolUse', { tool_use_id: 'plan', tool_name: 'ExitPlanMode' }],
    ['Notification', { notification_type: 'permission_prompt' }],
    ['Notification', { notification_type: 'elicitation_dialog' }],
  ]) {
    t.event(...e); assert.equal(t.state(), 'wait');
    t.event('PostToolUse', { tool_use_id: e[1].tool_use_id || 'done' }); assert.equal(t.state(), 'error');
  }
  t.advance(2500);
  t.event('UserPromptSubmit');
  t.event('Notification', { notification_type: 'idle_prompt' }); assert.equal(t.state(), 'think');
  t.event('PermissionRequest'); t.event('UserPromptSubmit'); assert.equal(t.state(), 'think');
});

test('Claude subagents keep working after the main Stop, without a completion burst', () => {
  const t = setup();
  t.event('SubagentStart', { agent_id: 'child' });
  t.event('SubagentStart', { agent_id: 'other' });
  t.event('Stop'); assert.equal(t.state(), 'working');
  t.event('SubagentStop', { agent_id: 'child' }); assert.equal(t.state(), 'working');
  t.event('SubagentStop', { agent_id: 'other' }); assert.equal(t.state(), 'idle');
});

test('Claude Stop and StopFailure clear pending work and burst for exactly 2500 ms', () => {
  for (const [event, state] of [['Stop', 'celebrate'], ['StopFailure', 'error']]) {
    const t = setup();
    t.event('PreToolUse', { tool_use_id: 'pending' }); t.event('PermissionRequest');
    t.event(event); assert.equal(t.state(), state);
    t.advance(2499); assert.equal(t.state(), state);
    t.advance(1); assert.equal(t.state(), 'idle');
  }
});

test('Claude finished tool tombstones suppress late PreToolUse and stay bounded', () => {
  const t = setup(); t.event('UserPromptSubmit');
  t.event('PostToolUse', { tool_use_id: 'old' });
  t.event('PreToolUse', { tool_use_id: 'old', tool_name: 'AskUserQuestion' }); assert.equal(t.state(), 'think');
  for (let i = 0; i < 256; i++) t.event('PostToolUse', { tool_use_id: String(i) });
  t.event('PreToolUse', { tool_use_id: '255' }); assert.equal(t.state(), 'think');
  t.event('PreToolUse', { tool_use_id: 'old' }); assert.equal(t.state(), 'working');
});

test('Claude staleness after 15 minutes clears tools, subagents, wait and phase', () => {
  const t = setup(); t.event('UserPromptSubmit');
  t.event('PreToolUse', { tool_use_id: '1' }); t.event('SubagentStart', { agent_id: 'child' });
  t.event('PermissionRequest'); t.advance(15 * 60 * 1000); assert.equal(t.state(), 'wait');
  t.advance(1); assert.equal(t.state(), 'idle');
  t.event('UserPromptSubmit'); assert.equal(t.state(), 'think');
  t.event('Stop'); assert.equal(t.state(), 'celebrate');
});

test('Claude multiple sessions aggregate wait > error > working > think > celebrate > idle', () => {
  const t = setup(); t.event('Stop', { session_id: 'done' }); assert.equal(t.state(), 'celebrate');
  t.event('UserPromptSubmit'); assert.equal(t.state(), 'think');
  t.event('PreToolUse', { session_id: 'b', tool_use_id: '1' }); assert.equal(t.state(), 'working');
  t.event('StopFailure', { session_id: 'failed' }); assert.equal(t.state(), 'error');
  t.event('PermissionRequest', { session_id: 'c' }); assert.equal(t.state(), 'wait');
  t.event('SessionEnd', { session_id: 'c' }); assert.equal(t.state(), 'error');
  t.advance(2500); assert.equal(t.state(), 'working');
  t.event('SessionEnd', { session_id: 'b' }); assert.equal(t.state(), 'think');
  t.event('SessionEnd'); assert.equal(t.state(), 'idle');
});

test('Claude malformed events and unknown sessions ending are ignored', () => {
  const t = setup();
  for (const e of [null, undefined, 1, 'bad', [], {}, { hook_event_name: 'UserPromptSubmit' },
    { hook_event_name: 'UserPromptSubmit', session_id: {} }]) t.tracker.event(e);
  for (const event of ['unknown', 'PreToolUse', 'SubagentStart', 'SessionEnd']) t.event(event);
  assert.equal(t.state(), 'idle');
  t.event('UserPromptSubmit'); t.advance(15 * 60 * 1000);
  t.event('unknown'); t.event('Notification', { notification_type: 'idle_prompt' });
  t.advance(1); assert.equal(t.state(), 'idle');
});
