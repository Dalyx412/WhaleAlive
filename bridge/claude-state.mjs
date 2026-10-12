const events = new Set([
  'SessionStart', 'SessionEnd', 'UserPromptSubmit', 'PreToolUse', 'PermissionRequest',
  'Notification', 'PostToolUse', 'PostToolUseFailure', 'SubagentStart', 'SubagentStop', 'Stop', 'StopFailure',
]);
const validId = value => typeof value === 'string' && value.length > 0;

// Retains activity only; the clock is injectable for deterministic expiry tests.
export function createClaudeTracker(now = Date.now) {
  const sessions = new Map();
  let burst = null;
  const reset = session => {
    session.phase = 'idle';
    session.wait = false;
    session.tools.clear();
    session.agents.clear();
  };
  const expire = (session, time) => {
    if (time - session.lastEventAt > 15 * 60 * 1000) reset(session);
  };
  return {
    event(e) {
      if (!e || !validId(e.session_id) || !events.has(e.hook_event_name)) return;
      const type = e.hook_event_name;
      if (type === 'SessionEnd') { sessions.delete(e.session_id); return; }
      if (type === 'Notification' && !['permission_prompt', 'elicitation_dialog'].includes(e.notification_type)) return;
      if (['PreToolUse', 'PostToolUse', 'PostToolUseFailure'].includes(type) && !validId(e.tool_use_id)) return;
      if (['SubagentStart', 'SubagentStop'].includes(type) && !validId(e.agent_id)) return;
      const time = now();
      let session = sessions.get(e.session_id);
      if (!session) {
        session = { phase: 'idle', wait: false, tools: new Set(), agents: new Set(), finished: new Set(), lastEventAt: time };
        sessions.set(e.session_id, session);
      }
      expire(session, time);
      session.lastEventAt = time;
      switch (type) {
        case 'UserPromptSubmit':
          session.phase = 'think'; session.wait = false; session.tools.clear(); break;
        case 'PreToolUse':
          if (session.finished.has(e.tool_use_id)) break;
          session.tools.add(e.tool_use_id);
          if (['AskUserQuestion', 'ExitPlanMode'].includes(e.tool_name)) session.wait = true;
          break;
        case 'PermissionRequest': case 'Notification': session.wait = true; break;
        case 'PostToolUse': case 'PostToolUseFailure':
          session.tools.delete(e.tool_use_id);
          session.finished.add(e.tool_use_id);
          if (session.finished.size > 256) session.finished.delete(session.finished.values().next().value);
          session.wait = false;
          break;
        case 'SubagentStart': session.agents.add(e.agent_id); break;
        case 'SubagentStop': session.agents.delete(e.agent_id); break;
        case 'Stop': case 'StopFailure':
          session.tools.clear(); session.wait = false; session.phase = 'idle';
          if (!session.agents.size) burst = { state: type === 'Stop' ? 'celebrate' : 'error', until: time + 2500 };
          break;
      }
    },
    state() {
      const time = now();
      const values = [];
      for (const session of sessions.values()) {
        expire(session, time);
        values.push(session.wait ? 'wait' : session.tools.size || session.agents.size ? 'working' : session.phase);
      }
      if (values.includes('wait')) return 'wait';
      if (burst?.state === 'error' && burst.until > time) return 'error';
      if (values.includes('working')) return 'working';
      if (values.includes('think')) return 'think';
      if (burst && burst.until > time) return burst.state;
      return 'idle';
    },
  };
}
