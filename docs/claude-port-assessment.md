# WhaleAlive → Claude Code integration assessment

> **Note:** this is the pre-implementation assessment. The implemented design is simpler than section 3 proposes: no launcher or per-run relay. The heartbeat owner lives inside `bridge/mcp-server.mjs` (enabled by `WHALE_ALIVE_CLAUDE=1`), and hooks send to `\\.\pipe\WhaleAlive.Claude`. See README "Claude Code 接入".

Assessed 2026-10-13 (Asia/Tokyo), repository `C:\coding\WhaleAlive`, HEAD `6ed03a300a98698162141bc706cad4d072a2ef46`.
References below are repository-relative `file:line` references at that revision. Proposed files/configuration are examples, not installed integration.

## 1. Conclusion and execution boundaries

**The existing stdio MCP server can be reused unchanged.** All 15 tools are independent of DSH. Automatic activity display needs a Claude hook adapter plus a persistent heartbeat owner. A clean port needs one C# source-dispatch change and generic UI labels; the named-pipe transport needs no change.

Only this assessment was added to source control's working tree; no existing tracked files were edited, staged, or committed. `npm ci` and the build generated ignored dependency/build outputs. Neither DSH nor Claude was launched or configured; `~/.claude`, `~/.dsh`, and global agent configuration were not accessed. `Connect-DSH.ps1` was read, not executed. No verification flags, including `--verify-shell` and `--verify-interaction`, were run. No icon, mouse, window, or autonomous movement permission was enabled.

Environment: Node `v24.16.0`, npm `11.13.0`; installed SDKs `6.0.428`, `8.0.425`, `9.0.306`, `10.0.203`. The requested unmodified build selected **SDK 10.0.203**, not 9.0.306. The project still targets `net9.0-windows10.0.19041.0` and explicitly outputs to `bin/Release/net9.0-windows` (`prototype/WhaleAlive.csproj:3-5`). This is not evidence of a build pinned to SDK 9.

The build emitted .NET's first-run notice, including automatic ASP.NET Core development-certificate installation. This was a tool-generated user-environment side effect of the requested build; no certificate trust command or global configuration edit was performed, and no cleanup of global state was attempted.

### Exact command output and results

Commands ran in the requested order. Console text is reproduced below with line endings normalized; exit codes are recorded separately.

`npm ci` — **PASS, exit 0** (audit warning, not a clean security audit):

```text
added 94 packages, and audited 95 packages in 2s

34 packages are looking for funding
  run `npm fund` for details

1 high severity vulnerability

To address all issues, run:
  npm audit fix --force

Run `npm audit` for details.
```

`npm run build` — **PASS, exit 0**:

```text
> whale-alive-prototype@0.1.0 build
> dotnet build prototype/WhaleAlive.csproj -c Release

欢迎使用 .NET 10.0!
---------------------
SDK 版本: 10.0.203

遥测
---------
.NET 工具会收集用法数据，帮助我们改善你的体验。它由 Microsoft 收集并与社区共享。你可通过使用喜欢的 shell 将 DOTNET_CLI_TELEMETRY_OPTOUT 环境变量设置为 "1" 或 "true" 来选择退出遥测。

阅读有关 .NET CLI 工具遥测的更多信息: https://aka.ms/dotnet-cli-telemetry

----------------
已安装 ASP.NET Core HTTPS 开发证书。
若要信任该证书，请运行 "dotnet dev-certs https --trust"
了解 HTTPS: https://aka.ms/dotnet-https

----------------
编写第一个应用: https://aka.ms/dotnet-hello-world
了解新增功能: https://aka.ms/dotnet-whats-new
浏览文档: https://aka.ms/dotnet-docs
报告问题并在 GitHub 上查找来源: https://github.com/dotnet/core
使用 "dotnet --help" 查看可用命令或访问: https://aka.ms/dotnet-cli
--------------------------------------------------------------------------------------
  正在确定要还原的项目…
  已还原 C:\coding\WhaleAlive\prototype\WhaleAlive.csproj (用时 2.14 秒)。
  WhaleAlive -> C:\coding\WhaleAlive\prototype\bin\Release\net9.0-windows\WhaleAlive.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:08.86
```

Started the exact `prototype/bin/Release/net9.0-windows/WhaleAlive.exe` once using PowerShell `Start-Process -WindowStyle Hidden -PassThru`, without arguments. No pet process was running beforehand. Waited four seconds, verified it had not exited, then ran `npm test`. A `finally` block stopped only the captured pet PID, including on test failure. Combined launch/test/cleanup output:

```text
Pet started PID=25596

> whale-alive-prototype@0.1.0 test
> node --test tests/*.test.mjs

✔ runtime bridge: permissions, cancellation, priority and preview immutability (62124.5299ms)
✔ MCP standard client discovers tools and calls the actual native bridge (294.2051ms)
✔ DSH multiple sessions preserve working/approval priority and completion expiry (0.4722ms)
✔ aborted turns do not celebrate; request errors expire; irrelevant data is ignored (0.1055ms)
✔ PNG filenames sort numerically without mutating input (0.7736ms)
✔ excluded frames never occur in the loop, including across wraparound (0.1809ms)
✔ empty sequence and single retained frame are safe (0.0948ms)
✔ non-looping playback ends on last retained frame even after delayed rendering (0.4962ms)
✔ ZIP stores exact PNG bytes with valid CRC, names and directory offsets (0.5346ms)
ℹ tests 9
ℹ suites 0
ℹ pass 9
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 64476.5796
npm test exit code: 0
Pet stopped PID=25596
```

**Test result: PASS, 9/9, zero failures/skips.** The long first test includes walking/carrying a preview. It deliberately requests real carrying and cursor interaction to verify rejection while permissions are false; it never enables them (`tests/bridge.test.mjs:9-27`). MCP discovery, retired-tool absence, default window/autonomy permissions, native status, schema rejection, and cursor denial are covered (`tests/bridge.test.mjs:30-48`). Two tracker tests and five frame-viewer tests complete the suite. Python tests and C# verification modes are outside `npm test`; Claude hooks and live DSH event delivery were not tested.

## 2. Existing transport and DSH behavior

### Named-pipe protocol

- Windows endpoint: `\\.\pipe\WhaleAlive.Prototype`; C# server name `WhaleAlive.Prototype`. Bidirectional byte-mode pipe, up to eight instances, asynchronous and `CurrentUserOnly`; no TCP listener (`bridge/pet.mjs:5`, `prototype/PipeBridge.cs:8-14`). Same-user access is not a separate agent identity/authentication boundary.
- One connection per request, one UTF-8 JSON object followed by LF; one LF-terminated JSON response, then close. This is a custom protocol, not MCP JSON-RPC. MCP exists only between the host and `mcp-server.mjs` over stdio (`bridge/pet.mjs:3-12`, `bridge/mcp-server.mjs:29`). Example: `{"action":"state","state":"think","source":"dsh"}\n` → `{"ok":true,"result":{"state":"think"}}\n`.
- Successful handler result is wrapped as `{ok:true,result:...}`; handler exceptions become `{ok:false,error:"..."}`. UI work is dispatched through WPF's dispatcher. The 8,192-byte buffer rejects a command reaching that size, so the LF-terminated request must fit within 8,191 bytes. Malformed JSON, EOF, or transport failures need not return an error object. An oversized-command exception is outside the inner handler-error wrapper (`prototype/PipeBridge.cs:14`).
- Node's default socket inactivity timeout is 600,000 ms; DSH overrides it to 1,000 ms. Server I/O uses an 11-minute cancellation token; awaiting the handler itself does not take that token. Timing out a socket does not itself cancel a native action (`bridge/pet.mjs:3-11`, `bridge/dsh-plugin.mjs:7`, `prototype/PipeBridge.cs:14`).
- Actions: `status`, `list`, `say(text)`, `state(state,source?)`, `stop`, `undo`, `cursor`, `carry(name,destination,real?,drag?)`; plus non-MCP `animations`, `animation(name,right?)`, `sit(name?,right?)` (`prototype/MainWindow.xaml.cs:82-115`). Valid states: `idle`, `think`, `working`, `wait`, `celebrate`, `sleep`, `error`.
- `companion` routes `operation`: `status`, `todo_add`, `todo_complete`, `borrow`, `find_cursor`, `hide_cursor`, `guide_cursor`, `deliver`, `windows`, `move_window`, `window_undo`, `plan_icons`, `preview_plan`, `apply_plan`, and non-MCP `adjust_putdown(name,right?)` (`prototype/MainWindow.xaml.cs:118-153`). Tool-specific fields follow below. Unknown actions/operations are rejected.

### Exact DSH event reduction

`dsh-plugin.mjs` is a Cordis-style plugin: `apply(ctx)` installs an effect, listeners, and a timer. `session/event` forwards `session?.id` and the event to a tracker; `agent/request-error` sets a shared error burst. Both then request immediate synchronization (`bridge/dsh-plugin.mjs:3-12`). There is no LLM call or transcript parsing.

| Tracker input | Change |
|---|---|
| Invalid/non-string session ID or absent event | Ignore. |
| `turn/start` | Set that session to `think`, including clearing its previous `wait`/`working`. |
| `tool/call` | Set to `working` only if that session already exists. |
| `turn/end`, `data.reason.kind === 'blocked'` | Set/retain that session as `wait`; does not delete it. |
| `turn/end`, reason `completed` | Delete session; replace shared burst with `celebrate` until `now()+2500`. |
| `turn/end`, reason `error` | Delete session; replace shared burst with `error` until `now()+2500`. |
| `turn/end`, any other/missing reason | Delete session; no new burst (an existing burst is not cleared). |
| `agent/request-error` | Replace shared burst with `error` for 2,500 ms, without deleting any session. |
| Other session events | No tracker change; plugin still attempts synchronization. |

Source: `bridge/dsh-state.mjs:5-15`, `bridge/dsh-plugin.mjs:8-9`. Only activity state is retained. A single shared burst means a later completion can replace an earlier error burst.

Aggregation order is **any wait > unexpired error burst > any working > any think > other unexpired burst > idle**. Thus a waiting session masks errors and work; completion does not mask another active session. Burst expiry is evaluated when `state()` is called, not by an independent 2.5-second timer (`bridge/dsh-state.mjs:16-23`). There is no tool-return → thinking transition and no stale-session eviction in this tracker.

### Heartbeat, progress, and the eight-second boundary

The **DSH plugin**, not the MCP server or pet, sends `{action:'state',state:tracker.state(),source:'dsh'}` immediately on installation, on events, and every **2,000 ms**, even when idle/unchanged. `inFlight` prevents overlapping requests; errors are swallowed, the next tick retries, and disposal stops the timer/listeners without sending a final idle message (`bridge/dsh-plugin.mjs:6-11`). A skipped in-flight event is represented by a later sync. Visible burst duration is consequently quantized by synchronization timing.

All valid state commands call `engine.SetAmbient`; **only `source === "dsh"`** also triggers approval behavior, updates `lastDsh`, feeds `DshProgress.Receive`, and refreshes task text (`prototype/MainWindow.xaml.cs:106`). `pet_state` and `pet.mjs state` omit `source`, so neither maintains this heartbeat/progress channel (`bridge/mcp-server.mjs:18`, `bridge/pet.mjs:20`). Sending `source:"claude"` today changes animation but does not connect task progress.

After **strictly more than eight seconds** without a recognized message, the two-second UI timer clears `lastDsh` and requests ambient `idle`; this normally occurs on the first tick after expiry, not exactly at eight seconds (`prototype/MainWindow.xaml.cs:17,38-42`). It does not invoke `Stop`, cancel a foreground carry, close the pipe, clear tracker sessions, or clear `pendingApproval` via `OnDsh`. Ambient display can remain deferred while busy/dragging (`prototype/Interaction.cs:33,40`). A queued approval notice can therefore outlive the connection until processed or otherwise cleared (`prototype/CompanionFeatures.cs:77-81,140-149`).

Separately, `DshProgress.Text` reports never-connected before any receive and disconnected after >8 seconds. Every receive refreshes its timestamp. `think/working/wait` clear the remembered outcome; `celebrate/error` set it; `idle` preserves it, so idle heartbeats keep “recently completed/failed” visible. This is stage reporting, not percentage completion (`prototype/DshProgress.cs:3-28`). The health JSON described below has no role in this watchdog.

### All 15 MCP tools

All rows below are **DSH-independent**: no DSH APIs, event types, or credentials in their implementation. `?` means optional in the schema; all other inputs are required. “None” means `{}`. Bounds are schema string lengths, not UTF-8 pipe byte limits.

| Tool | Inputs | Native action / behavior | Reference |
|---|---|---|---|
| `desktop_list_icons` | None | `list`: read actual desktop icon IDs, names, coordinates and auto-arrange status. | `bridge/mcp-server.mjs:14` |
| `desktop_carry_icon` | `name` 1–1024; `destination` ∈ `右下角/左下角/右上角/桌面中央`; `real=false`; `drag=false` | `carry`: animate a copy or move the real icon position with undo; real mode requires manual pet permission. Does not relocate files. | `bridge/mcp-server.mjs:15` |
| `pet_status` | None | `status`: busy/ambient state, runtime permissions, animation/render fields, undo-history count. | `bridge/mcp-server.mjs:16` |
| `pet_say` | `text` 1–80 | `say`: short speech bubble. | `bridge/mcp-server.mjs:17` |
| `pet_state` | `state` ∈ `idle/think/working/wait/celebrate/sleep/error` | `state`: set ambient animation; deferred during active interaction. No `source`, so no DSH progress/heartbeat update. | `bridge/mcp-server.mjs:18` |
| `pet_stop` | None | `stop`: cancel current foreground/ambient activity and release interaction; not application exit. | `bridge/mcp-server.mjs:19` |
| `pet_undo` | None | `undo`: restore the latest undoable real icon move. | `bridge/mcp-server.mjs:20` |
| `cursor_grab` | None | `cursor`: permission-gated brief mouse pull after three-second countdown; description specifies ≤0.9 s, escape on movement/click/double Esc. | `bridge/mcp-server.mjs:21` |
| `pet_companion_status` | None | `companion/status`: todos, saved paths, window/autonomy permissions, sleep settings/phase. | `bridge/mcp-server.mjs:22` |
| `pet_todo` | `operation` ∈ `todo_add/todo_complete`; `text?` ≤160; `id?` ≤64 | Add persistent todo or mark by ID complete. Native add requires nonblank text; unknown completion ID is a no-op. | `bridge/mcp-server.mjs:23`; `prototype/CompanionStore.cs:55-68` |
| `pet_deliver_artifact` | `path` 3–2048 | `companion/deliver`: retain an existing absolute local file/directory path and animate delivery; no execution/upload by this action. | `bridge/mcp-server.mjs:24`; `prototype/CompanionStore.cs:70-78` |
| `pet_play` | `operation` ∈ `borrow/hide_cursor/find_cursor/guide_cursor`; `name?` ≤1024 | Borrow icon copy; pet follows/reveals pointer for hide/find; guide moves pointer toward an icon with cursor permission and does not click. Borrow/guide need a resolvable icon name. | `bridge/mcp-server.mjs:25`; `prototype/CompanionFeatures.cs:111-138` |
| `pet_list_windows` | None | `companion/windows`: list eligible ordinary windows. Read only. | `bridge/mcp-server.mjs:26` |
| `pet_window` | `operation` ∈ `move_window/window_undo`; `handle?` positive integer; `direction?` ∈ `left/right` | Move a listed window with manual window permission, or undo. Move needs handle; omitted direction currently becomes right. Undo checks original process/current position. | `bridge/mcp-server.mjs:27`; `prototype/MainWindow.xaml.cs:144-147`; `prototype/CompanionFeatures.cs:162-190` |
| `pet_icon_plan` | `operation` ∈ `plan_icons/preview_plan/apply_plan`; `names?` array 1–12, each 1–1024 | Generate selected-icon layout, preview markers, or apply existing plan with real-icon permission. Generation needs names. Review-before-apply is a tool instruction, not a separate native approval token. | `bridge/mcp-server.mjs:28`; `prototype/CompanionFeatures.cs:24-56` |

The server registers as `whale-alive` version `0.1.0`; the host registration key can be `whale_alive`. All calls convert pipe responses to MCP text content and `isError:!r.ok`; transport errors also become MCP errors. A canceled MCP request sends a **global pet `stop`**, not a per-request cancellation (`bridge/mcp-server.mjs:6-12`). Only list/status/companion-status/window-list tools have `readOnlyHint:true`. Optional schema fields still have operation-dependent native requirements; the port should initially preserve this behavior.

### DSH health report and connector

`dsh-health-v04.mjs` injects DSH's `tools` service, checks `ctx.tools.get('mcp__whale_alive__'+name)` for the 15 expected tools, and detects four retired registrations: `pet_focus`, `pet_memory`, `pet_weather_bottle`, `pet_icon_scene` (`bridge/dsh-health-v04.mjs:4-16`). Immediately and every five seconds, it writes `artifacts/dsh-connection.json.tmp` then renames it to `artifacts/dsh-connection.json`. Fields: ISO `updatedAt`, `expected:15`, `registered` names, `retiredRegistered` names, and `ready` true only when all 15 and no retired tools are registered. In-flight writes are suppressed; exceptions are ignored; disposal stops the timer (`bridge/dsh-health-v04.mjs:17-24`).

This is **DSH registry diagnostics**, not a pet heartbeat, pipe round trip, tool execution test, or proof that Claude has loaded tools. The file may persist stale after shutdown; inspect `updatedAt`. It was absent during this assessment. Tracked references are the producer and documentation, not a C# consumer. `artifacts/` is ignored (`.gitignore:4`).

`Connect-DSH.ps1` finds a source tree whose package name is `@deepseek-ai/dsh-root`, creates `%LOCALAPPDATA%\WhaleAlive\DSH\whale-alive.cordis.yml`, and inserts state plugin, `@deepseek-ai/dsh-mcp-client` stdio client (`serverName: whale_alive`, `node`, absolute server path, repo cwd, 600,000 ms tool timeout), and health plugin. It optionally installs missing MCP dependencies, starts the pet, and runs `pnpm dsh web --patch <overlay>`; `-GenerateOnly` exits after writing the overlay; `-NoOpen` becomes `--no-open` (`Connect-DSH.ps1:11-32,41-69,71-95`). These Cordis/DSH parts must be replaced, not loaded into Claude. The feature summary agrees on 15 tools and per-machine overlay/report paths (`COMPANION-FEATURES.md:16-22`).

## 3. Proposed Claude Code integration

### MCP registration: reuse the current server

For a future implementation, merge this entry into the chosen Claude project's **project-root `.mcp.json`** (not a home-directory file):

```json
{
  "mcpServers": {
    "whale_alive": {
      "type": "stdio",
      "command": "node",
      "args": ["D:/coding/WhaleAlive/bridge/mcp-server.mjs"]
    }
  }
}
```

Equivalent future PowerShell command, run from that project root:

```powershell
claude mcp add --transport stdio --scope project whale_alive -- node "D:/coding/WhaleAlive/bridge/mcp-server.mjs"
```

These are alternatives, not steps to run twice. Project scope writes `.mcp.json`; default/local and user scopes use `~/.claude.json`, so neither is appropriate here. Interactive Claude may request approval of the project MCP server. This does not grant WhaleAlive's movement permissions. See [Anthropic MCP registration and scopes](https://code.claude.com/docs/en/mcp#project-scope). No registration command was executed.

Use a resolved `node.exe` path in generated config if PATH is unreliable. The server's relative import resolves from its module, so it needs no DSH `cwd` setting. The pet must run independently; MCP startup alone does not launch it. Claude's eventual discovery check should show the same 15 tools, then successfully call `pet_status`; this assessment's standard MCP-client test already establishes transport interoperability, not Claude-specific acceptance.

### Hook delivery and fast-path contract

Verified hook contract: command hooks receive event JSON on stdin. `session_id`/`hook_event_name` identify events; tool events can include `tool_use_id`, while `PermissionRequest` lacks it. Current docs support direct executable `command` + `args` and `async:true`. Stop means response completion, not verified task success, and excludes user interrupts. Consult [Anthropic's hook reference](https://code.claude.com/docs/en/hooks).

**Proposed architecture:** `Claude hook → short Node sender → local relay → existing pet pipe`; Claude's MCP process remains separate. Do not call an LLM, MCP tool, PowerShell launcher, or the potentially ten-minute `request()` helper from each hook. All hooks are observational: stdout empty, exit 0, never return allow/deny/block decisions or change permissions.

Add `bridge/claude-hook.mjs`: consume stdin with a bounded buffer (e.g. 1 MiB), validate JSON, copy only session/event/agent/tool IDs, tool name, notification kind, lifecycle reason/source, optional prompt ID and booleans such as interruption. For `mcp__whale_alive__pet_state` only, also allowlist its seven-value `tool_input.state` enum for the manual override below. Discard all other tool arguments/results, prompt text, messages, transcript paths, cwd, and credentials; never open transcripts. Send one small authenticated event to the relay and exit. Oversize/malformed input, missing relay, or IPC failure must not affect Claude.

Target <200 ms end-to-end per invocation: direct Node exec, no heavy imports, a 100 ms IPC deadline and a 150 ms script lifetime watchdog, no retry/sleep loop. Timer expiry destroys socket/stdin and exits silently. Cold process startup is outside the script timer: **the <200 ms goal requires measurement on Windows**, not a guarantee from these numbers. Configure `async:true` for non-blocking Claude execution; enforce the deadline inside the script because async hook timeout is not enforced by Claude ([background-hook documentation](https://code.claude.com/docs/en/hooks#run-hooks-in-the-background)).

Example future **project-local** `.claude/settings.local.json` entry (merge, never replace unrelated settings):

```json
{
  "hooks": {
    "PreToolUse": [{"matcher":"*","hooks":[{
      "type":"command","command":"node",
      "args":["D:/coding/WhaleAlive/bridge/claude-hook.mjs"],"async":true
    }]}]
  }
}
```

Generate the same handler for every event in the table below. Omit matchers for lifecycle events; use `*` for tools/subagents and filter `notification_type` inside the sender. Use absolute script/executable paths to survive cwd changes and spaces. Confirm the installed CLI accepts the current documented exec-form schema; no `claude` executable was found on this session's PATH, so version/schema compatibility is untested. Async `SessionEnd` is best-effort; cleanup must not depend on its delivery.

### Proposed event-to-state policy

This table is adapter design, not behavior already present in WhaleAlive. Maintain records keyed by session, optional prompt generation, agent, and tool-use ID; never treat every hook as an unconditional global state overwrite. Keep per-agent in-flight tool sets and wait markers. Aggregate **wait > temporary error > active tool/child work > thinking > temporary celebrate > idle**.

| Hook | Proposed reducer behavior |
|---|---|
| `SessionStart` | Startup/resume: create an idle record. Clear/replacement: retire the old generation. Compact: preserve existing active work if the same session continues; do not spuriously celebrate/reset it. Relay ownership is established by the launcher before this event. |
| `UserPromptSubmit` | Begin a prompt generation, clear stale outcome/wait markers for the main turn, mark main agent `think`; preserve actually running background children. |
| `PreToolUse` | Add tool ID; normally `working`. Treat `AskUserQuestion` and approval-oriented `ExitPlanMode` as `wait` until resolved. Do not clear unrelated agents' waits. |
| `PermissionRequest` | Mark that agent/tool-name as `wait`; observe only, never decide permission. Correlate conservatively with pending tools because this input lacks a tool-use ID. |
| `PostToolUse` | Remove matching tool and its wait; remain `working` if other tools/children run, otherwise return the active turn to `think`. No completion celebration here. |
| `Notification` | `permission_prompt`, `elicitation_dialog`, `elicitation_url_dialog`, `agent_needs_input` → scoped `wait`; `idle_prompt` → idle only if no active work/wait; auth/unknown notices → no change. Completion notices cannot finish the whole session. Ignore free-text message content. |
| `Stop` | Close the main response generation and its main-tool markers; if other work remains, keep aggregate work/wait. Otherwise emit `celebrate` for 2.5 s then idle. Interpret celebration as “response finished,” not “goal verified.” Repeated Stop is idempotent and never requests continuation. |
| `SubagentStop` | Retire only the identified child and its tool/wait records; recompute parent state. Never clear the whole session or celebrate merely because one child stopped. |
| `SessionEnd` | Remove only that session's records and tombstone its generation. Send aggregate state for any survivors; if none, best-effort idle then cease heartbeat. Launcher still owns process cleanup. |
| `SubagentStart` (additional) | Track child `think`/active work so a main Stop cannot erase background activity. |
| `PostToolUseFailure` (additional) | Clear matching tool/wait; interruption → no celebration; other failure → 2.5 s error, then remaining work/think. |
| `StopFailure` (additional, version-gated) | Retire failed main turn and emit temporary error while preserving other work. |

`PermissionRequest` is the preferred prompt signal; notification delivery can lag. There is no general permission-approved event in the listed set: a long permitted tool may remain visually `wait` until its post event. Do not invent an approval from elapsed time. Test denial, retry, and non-interactive behavior against the installed CLI; newer notification/error events must be version-gated. Hook semantics and available payloads are documented in [PermissionRequest](https://code.claude.com/docs/en/hooks#permissionrequest), [Notification](https://code.claude.com/docs/en/hooks#notification), and [Stop](https://code.claude.com/docs/en/hooks#stop).

Because async hook processes can arrive out of order, use completed-tool tombstones (Post before Pre must not resurrect a tool), closed-agent/session generations, optional prompt IDs, and idempotent lifecycle handling. A sender timestamp is useful for diagnostics but is not authoritative Claude event ordering. Suppress tool activity overlays for `mcp__whale_alive__pet_state` so its post hook does not immediately override an explicit state choice. On successful Post, retain its allowlisted requested state as a five-second manual override and heartbeat that state; waits take precedence. Failure does not establish an override. These ordering/override rules need fixtures before release.

### Persistent heartbeat and process ownership

Add `bridge/claude-relay.mjs` with an in-memory reducer (`bridge/claude-state.mjs`). `Connect-Claude.ps1` starts exactly one hidden relay, waits for readiness before launching Claude, and supplies its random pipe name/token to that child environment and therefore the hooks. Suggested endpoint: `\\.\pipe\WhaleAlive.Claude.<random-run-id>`. Authenticate the first event with a random per-run secret; never log it. Restrict the pipe to the current user where the implementation supports ACLs; a randomized name alone is not authentication. No public TCP listener or persistent global config is needed.

The relay acknowledges queued events immediately, coalesces updates, and sends the reduced state to the pet immediately on changes and every **two seconds**, including idle between prompts and long thinking/tool/permission waits. Use `request(command,1000)` with a single in-flight call; retain the newest pending state and retry on the next tick if the pet is absent. Schedule burst expiry explicitly so a 2.5-second celebration does not depend only on the next heartbeat tick. A same-state heartbeat preserves progress freshness.

For the recommended C# change, send `{action:"state",state,source:"claude"}`. With an unchanged binary, an explicit compatibility mode can send `source:"dsh"`; this gives progress/watchdog behavior but misleading DSH labels. A hooks-only script that exits after one state message cannot satisfy the eight-second watchdog during long operations. Removing the watchdog would hide dead integrations and is not recommended.

The launcher owns the relay PID, restores only its process-scoped environment in `finally`, and terminates only processes it started. The relay independently checks the launcher process identity/liveness every two seconds and exits if its owner disappears. On the last SessionEnd/normal CLI exit, best-effort idle then stop heartbeats; the existing UI subsequently says disconnected. Do not expire healthy sessions merely because hooks are quiet: long tools and approval waits are legitimate. Heartbeats prove relay liveness, not model progress.

For v1, enforce one active integration controller per Windows user with an OS named mutex, across repo copies; do not run the DSH state plugin concurrently. Aggregate children/sessions inside that controller. Supporting independently launched Claude terminals simultaneously needs a shared, user-scoped broker with per-client leases, not several relays racing on the pet's single ambient state. Abrupt hook loss/user interruption while Claude stays alive remains a semantic-staleness risk; process liveness alone cannot solve it.

### Proposed files and minimum C# work

| File | Future change |
|---|---|
| `bridge/claude-hook.mjs` (new) | Fast stdin sanitizer and bounded relay sender. |
| `bridge/claude-state.mjs` (new) | Pure reducer, concurrency/tombstones, bursts; use policy above rather than forcing incompatible Claude events into `dsh-state.mjs`. |
| `bridge/claude-relay.mjs` (new) | Own state/timer, receive events, enforce lifecycle and forward pet requests. |
| `Connect-Claude.ps1` (new) | Generate project-scoped config snippets; resolve paths, validate dependencies, acquire ownership mutex, start relay and optionally absent pet, launch CLI, cleanup owned children. `-GenerateOnly` must not launch/configure a global agent. No home-config reads/writes or implicit npm fixes. |
| `.mcp.json`, project `.claude/settings.local.json` (future additions/merges) | MCP and hook registrations above; generate only for an explicitly selected project. No files were added there in this assessment. |
| `prototype/MainWindow.xaml.cs:106` | Accept recognized agent sources `dsh` and `claude` through the existing progress/approval/watchdog branch. Keep unsourced `pet_state` behavior. No need to alter `PipeBridge.cs`. |
| `prototype/DshProgress.cs:19-28` | Replace DSH wording with generic “AI agent/智能体,” or pass a validated display label. Type/field renaming is optional cleanup. |
| Other visible DSH text | Genericize `prototype/CompanionFeatures.cs:56,148`, `prototype/CompanionPanel.cs:81,83,130`, `prototype/CompanionPanel.SocialMusic.cs:37`, and fallback `prototype/PetWindow.Pointer.cs:60`. Internal `OnDsh`/field names can initially remain. |
| `prototype/MainWindow.xaml.cs:41` (recommended hardening) | On disconnect also clear approval tracking through the generic equivalent of `OnDsh("idle")`; retain foreground-action behavior and the eight-second watchdog. |
| `tests/claude-state.test.mjs`, `tests/claude-hook.test.mjs` (new) | Reducer ordering/parallelism, malformed input, IPC absence/deadline, liveness/cleanup, and privacy fixtures. Extend native progress checks for accepted source and disconnect clearing. |

Do not port `dsh-health-v04.mjs` verbatim: Claude does not expose its `ctx.tools` registry. If diagnostics are needed, add a separate optional `artifacts/claude-connection.json` with distinct fields for relay liveness, latest hook, and latest successful pet reply. Label any standalone SDK `tools/list` probe as server validation; only a check from the actual Claude session establishes Claude-side tool availability. Never manufacture a `ready:true` by copying the DSH report.

## 4. Risks, open questions, and acceptance boundary

- **Not implemented:** no Claude binary on PATH, no authenticated Claude session, no hook-latency measurements or end-to-end port validation. Pin/test the intended CLI version before choosing newer payload fields and exec-form hooks. Official web documentation was checked on the assessment date.
- **Event fidelity:** Stop can be followed by continuation from other hooks; user interruption may emit no Stop. Permission approval has no direct resume signal here. Async reordering and missed events can leave plausible but stale state. Clarify whether rough activity stages suffice before adding deeper instrumentation.
- **Heartbeat arbitration:** fixed pet pipe/global state and global MCP cancellation do not isolate users' concurrent agent tasks. Define ownership and explicit `pet_state` override behavior; a heartbeat will otherwise overwrite manual state within two seconds. Do not claim percent-complete or true task success.
- **Payload sizes:** schema character limits and the pipe's 8 KiB cap differ. Twelve long/CJK icon names can pass MCP schema yet exceed native framing. Existing timeout/error behavior is a pre-existing limitation, not a Claude dependency.
- **Windows locality:** native Windows Node and an interactive desktop session are assumed. WSL/remote Claude cannot be assumed to reach this Windows named pipe. Absolute paths and Node resolution must be tested after moving the checkout.
- **Permissions/privacy:** keep the pet's manual gates and default-off flags. MCP descriptions are instructions, not proof of user approval; icon-plan review is not enforced by a review token. MCP status can return saved paths/todos, so automatic hooks must not fetch or relay these contents. Never forward hook prompts/tool output to diagnostics.
- **Existing dependency warning:** `npm ci` reported one high-severity vulnerability. No audit fix, lockfile change, or vulnerability triage was performed; assess the advisory separately before deployment.
- **Future acceptance:** test cold/warm hook latency (<200 ms target), idle/thinking/tool/wait lasting >8 s, burst expiry, pet absence/restart, permission denial/approval, failed/interrupted tools, child completion while parent works, out-of-order hooks, CLI crash, and no orphan relay. Verify all 15 tools in Claude and use read-only/preview/denial cases with permissions off. Retain this run's 9/9 test result as the unchanged baseline, not evidence the proposed port already works.
