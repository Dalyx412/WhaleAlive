import test from 'node:test';
import assert from 'node:assert/strict';
import { request } from '../bridge/pet.mjs';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { fileURLToPath } from 'node:url';
import net from 'node:net';

test('runtime bridge: permissions, cancellation, priority and preview immutability', async () => {
  const status=await request({action:'status'});assert.equal(status.ok,true);
  assert.equal(status.result.animationCount,23);
  assert.equal(status.result.realMoveAllowed,false);assert.equal(status.result.cursorAllowed,false);
  const before=await request({action:'list'});assert.ok(before.result.icons.length>0);
  const icon=before.result.icons.find(i=>i.Name==='Steam')??before.result.icons[0];
  const denied=await request({action:'carry',name:icon.Name,destination:'右下角',real:true});assert.equal(denied.ok,false);assert.match(denied.error,/开启/);
  const cursor=await request({action:'cursor'});assert.equal(cursor.ok,false);assert.match(cursor.error,/开启/);
  const missing=await request({action:'carry',name:'nonexistent-whale-test-icon',destination:'右下角'});assert.equal(missing.ok,false);
  const invalid=await request({action:'carry',name:icon.Name,destination:'invalid'});assert.equal(invalid.ok,false);
  const pending=request({action:'carry',name:icon.Name,destination:'左下角'});
  await new Promise(r=>setTimeout(r,350));
  const competing=await request({action:'carry',name:icon.Name,destination:'右下角'});assert.equal(competing.ok,false);
  await request({action:'state',state:'think'});
  const during=await request({action:'status'});assert.equal(during.result.busy,true);assert.notEqual(during.result.state,'think');
  await request({action:'stop'});const cancelled=await pending;assert.equal(cancelled.ok,false);
  const afterStop=await request({action:'status'});assert.equal(afterStop.result.busy,false);
  await request({action:'state',state:'idle'});
  const preview=await request({action:'carry',name:icon.Name,destination:'右下角'});assert.equal(preview.ok,true);
  const after=await request({action:'list'});assert.deepEqual(after.result.icons,before.result.icons);
});

test('MCP standard client discovers tools and calls the actual native bridge',async()=>{
 const client=new Client({name:'whale-alive-test',version:'1.0'});
 const transport=new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL('../bridge/mcp-server.mjs',import.meta.url))]});
 try {
   await client.connect(transport);const list=await client.listTools();assert.equal(list.tools.length,15);
   for(const name of ['pet_focus','pet_memory','pet_weather_bottle','pet_icon_scene'])assert.ok(!list.tools.some(t=>t.name===name));
   for(const operation of ['focus_start','weather','memory_add','nest','stage']){
     const retired=await request({action:'companion',operation});assert.equal(retired.ok,false);
   }
   assert.ok(list.tools.find(t=>t.name==='desktop_carry_icon'));
   assert.ok(list.tools.find(t=>t.name==='pet_deliver_artifact'));
   const companion=await client.callTool({name:'pet_companion_status',arguments:{}});assert.equal(companion.isError,false);
   assert.equal(JSON.parse(companion.content[0].text).result.windowMoveAllowed,false);
   assert.equal(JSON.parse(companion.content[0].text).result.autonomousMoveAllowed,false);
   const result=await client.callTool({name:'pet_status',arguments:{}});assert.equal(result.isError,false);
   assert.equal(JSON.parse(result.content[0].text).result.busy,false);
   const invalid=await client.callTool({name:'desktop_carry_icon',arguments:{name:'Steam',destination:'unknown',real:false}});assert.equal(invalid.isError,true);
   const denied=await client.callTool({name:'cursor_grab',arguments:{}});assert.equal(denied.isError,true);
 }finally{await client.close();}
});

// A Claude Code session with the WhaleAlive hooks owns this pipe and heartbeats the real pet,
// which would override the states this test sets (e.g. when npm test runs from such a session).
const liveClaudeRelay = await new Promise(resolve => {
  const socket = net.createConnection('\\\\.\\pipe\\WhaleAlive.Claude');
  socket.once('connect', () => { socket.destroy(); resolve(true); });
  socket.once('error', () => resolve(false));
});
test('Claude and DSH heartbeats preserve manual state, reset on changes and recover after watchdog', { skip: liveClaudeRelay && 'a live Claude Code session is driving the pet' }, async () => {
  for (const source of ['claude', 'dsh']) {
    await request({ action: 'state', state: 'think', source });
    await request({ action: 'state', state: 'sleep' });
    await request({ action: 'state', state: 'think', source });
    assert.equal((await request({ action: 'status' })).result.state, 'sleep');
    await request({ action: 'state', state: 'working', source });
    assert.equal((await request({ action: 'status' })).result.state, 'working');
    await request({ action: 'state', state: 'wait', source });
  }
  // The watchdog runs on a two-second UI timer, strictly after eight seconds.
  await new Promise(resolve => setTimeout(resolve, 10500));
  assert.equal((await request({ action: 'status' })).result.state, 'idle');
  await request({ action: 'state', state: 'wait', source: 'claude' });
  assert.equal((await request({ action: 'status' })).result.state, 'wait');
  await request({ action: 'state', state: 'idle', source: 'claude' });
});
