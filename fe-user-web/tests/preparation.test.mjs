import test from 'node:test';
import assert from 'node:assert/strict';
import { PreparationClient } from '../src/services/preparationClient.ts';
import { ApiError } from '../src/services/api.ts';
const content=(id=1,language='vi')=>({poiId:id,requestedLanguage:language,actualLanguage:language,isFallback:false,text:'Fixture',audio:{id:1,language,url:`https://fixture.test/${language}.wav`}});
const item=(status='Ready',id=1,language='vi')=>({poiId:id,language,sourceVersion:'fixture',status,textReady:true,audioReady:status==='Ready',
  jobId:status==='Queued'||status==='Processing'?'job-id':null,statusUrl:null,errorCode:null,retryAt:null,requiresOperatorAction:false,content:status==='Ready'?content(id,language):null});
const result=data=>({data,status:data.status==='Queued'?202:200,retryAfterMs:0});
const deferred=()=>{let resolve;const promise=new Promise(r=>{resolve=r;});return {promise,resolve};};
function setup(overrides={}) {
  const updates=[];const counts={prepare:0,status:0,nearby:0,content:0};
  const api={prepare:async(id,lang)=>{counts.prepare++;return result(item('Ready',id,lang));},
    status:async()=>{counts.status++;return result(item());},
    nearby:async()=>{counts.nearby++;return {data:{items:[item()]},status:200,retryAfterMs:0};},
    content:async()=>{counts.content++;return content();},...overrides};
  const client=new PreparationClient((key,state)=>updates.push({key,state}),api);client.pollAfterMs=5;
  client.setInterest('selection',['1:vi']); return {client,updates,counts};
}
test('concurrent clicks share a single preparation POST and cache successful state',async()=>{
  const wait=deferred();let calls=0;
  const {client}=setup({prepare:async()=>{calls++;return wait.promise;}});
  const first=client.prepareLanguage(1,'vi'); const duplicate=client.prepareLanguage(1,'vi');
  assert.equal(calls,1);wait.resolve(result(item()));await Promise.all([first,duplicate]);
  await client.prepareLanguage(1,'vi');assert.equal(calls,1);client.cancelAll();
});
test('GenerationPaused stops repeated POSTs for missing content and keeps existing text',async()=>{
  let calls=0;
  const {client}=setup({prepare:async()=>{calls++;return {data:{...item('Unavailable'),errorCode:'GenerationPaused',content:{...content(),audio:null}},status:503,retryAfterMs:0};}});
  await client.prepareLanguage(1,'vi');await client.prepareLanguage(1,'vi',true);
  client.setInterest('selection',['2:en']);await client.prepareLanguage(2,'en');
  await client.prepareNearby({latitude:0,longitude:0},[2]);
  assert.equal(calls,1);assert.equal(client.states.get('1:vi').item.content.text,'Fixture');
});
test('accepted job honors delay and polls to Ready with no further POSTs',async()=>{
  let status=0;const stamps=[];const start=Date.now();
  const {client}=setup({prepare:async()=>({...result(item('Queued')),retryAfterMs:15}),status:async()=>{stamps.push(Date.now());return result(item(++status===1?'Processing':'Ready'));}});
  await client.prepareLanguage(1,'vi');assert.equal(status,2);assert.ok(stamps[0]-start>=14);
  assert.equal(client.states.get('1:vi').item.status,'Ready');
});
test('selection change aborts polling before another request and ignores an old POST response',async()=>{
  const wait=deferred();const {client,updates}=setup({prepare:async()=>wait.promise});
  const pending=client.prepareLanguage(1,'vi');client.setInterest('selection',['2:en']);
  wait.resolve(result(item()));await pending;assert.equal(updates.length,0);
  const other=setup({prepare:async()=>result(item('Queued'))});other.client.pollAfterMs=100;
  const poll=other.client.prepareLanguage(1,'vi');await Promise.resolve();other.client.setInterest('selection',[]);await poll;
  assert.equal(other.counts.status,0);
});
test('429 obeys cooldown; only an explicit later request can retry',async t=>{
  let now=100000; t.mock.method(Date,'now',()=>now); let calls=0;
  const {client}=setup({prepare:async()=>{calls++;throw new ApiError(429,{code:'RequestRateLimited'},2000);}});
  await client.prepareLanguage(1,'vi');await client.prepareLanguage(1,'vi',true);assert.equal(calls,1);
  now+=2001;await client.prepareLanguage(1,'vi',true);assert.equal(calls,2);
});
test('GPS ticks do not repeat nearby POSTs; no foreign languages are prepared',async()=>{
  const {client,counts}=setup();client.setInterest('nearby',['1:vi','2:vi']);
  await client.prepareNearby({latitude:0,longitude:0},[1,2]);
  for(let index=0;index<20;index++) await client.prepareNearby({latitude:0,longitude:0},[1,2]);
  assert.equal(counts.nearby,1);assert.equal(counts.prepare,0);
});
test('job 404 reads current stored content once and does not re-enqueue',async()=>{
  const {client,counts}=setup({prepare:async()=>result(item('Queued')),status:async()=>{throw new ApiError(404);}});
  await client.prepareLanguage(1,'vi');assert.equal(counts.content,1);assert.equal(client.states.get('1:vi').item.status,'Ready');
});
test('polling has a finite deadline and cancelAll releases every interest',async()=>{
  let calls=0;
  const {client,counts,updates}=setup({prepare:async()=>{calls++;return result(item('Queued'));}});client.deadlineMs=4;
  await client.prepareLanguage(1,'vi');assert.equal(counts.status,0);assert.match(client.states.get('1:vi').message,/hết thời gian/);
  const finishedUpdates=updates.length;client.setInterest('selection',['1:vi']);await Promise.resolve();
  assert.equal(updates.length,finishedUpdates);
  client.cancelAll();await client.prepareLanguage(2,'en');assert.equal(calls,1);
});
