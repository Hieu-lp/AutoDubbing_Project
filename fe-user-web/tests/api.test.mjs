import test from 'node:test';
import assert from 'node:assert/strict';
import { ApiError, apiRequest, isReady, retryDelay, visitorApi } from '../src/services/api.ts';
import { createWalkingRoute } from '../src/services/routeService.ts';

test('fallback text/audio never counts as requested language ready', () => {
  const content={poiId:1,requestedLanguage:'en',actualLanguage:'vi',isFallback:true,text:'Nguồn',audio:{language:'vi',url:'https://fixture.test/vi.wav'}};
  assert.equal(isReady(content,'en'),false);
  assert.equal(isReady({...content,isFallback:false,actualLanguage:'en',audio:{language:'en',url:'https://fixture.test/en.wav'}},'en'),true);
});
test('Retry-After handles seconds, dates and invalid values', () => {
  assert.equal(retryDelay('30'),30000); assert.equal(retryDelay(null),0);
  assert.equal(retryDelay(new Date(50000).toUTCString(),10000),40000); assert.equal(retryDelay('invalid'),0);
});
test('anonymous API request contains only backend contract, never credentials', async t => {
  t.mock.method(globalThis,'fetch',async (url,options)=>{
    assert.equal(url,'/api/pois/1/prepare-content'); assert.equal(options.credentials,'omit');
    assert.equal(options.headers.Authorization,undefined); assert.equal(options.headers.apikey,undefined);
    assert.deepEqual(JSON.parse(options.body),{language:'en'});
    return Response.json({poiId:1,status:'Unavailable',errorCode:'GenerationPaused'},{status:503});
  });
  const result=await visitorApi.prepare(1,'en'); assert.equal(result.status,503); assert.equal(result.data.errorCode,'GenerationPaused');
});
test('429 Problem Details preserves Retry-After instead of silently retrying', async t => {
  const fetch=t.mock.method(globalThis,'fetch',async ()=>Response.json({code:'RequestRateLimited'},{status:429,headers:{'Retry-After':'10'}}));
  await assert.rejects(apiRequest('pois/prepare-nearby',{body:{latitude:0,longitude:0}}),error=>error instanceof ApiError && error.status===429 && error.retryAfterMs===10000);
  assert.equal(fetch.mock.callCount(),1);
});
test('aborting a request is distinguished from a network error', async t => {
  t.mock.method(globalThis,'fetch',async (_url,{signal})=>new Promise((resolve,reject)=>signal.addEventListener('abort',()=>reject(new DOMException('Aborted','AbortError')))));
  const controller=new AbortController(); const pending=apiRequest('pois',{signal:controller.signal}); controller.abort();
  await assert.rejects(pending,{name:'AbortError'});
});
test('walking route uses only backend, all GeoJSON vertices, returned order and walking totals', async t => {
  t.mock.method(globalThis,'fetch',async (url,options)=>{
    assert.equal(url,'/api/routes/preview');
    assert.deepEqual(JSON.parse(options.body),{startLatitude:10,startLongitude:106,poiIds:[2,1]});
    return Response.json({geometry:{type:'LineString',coordinates:[[106,10],[106.2,10.1],[106.3,10.2]]},
      stops:[{poiId:1,order:1},{poiId:2,order:2}],totalDistanceMeters:1234,totalDurationSeconds:777,attribution:'ORS fixture'});
  });
  const result=await createWalkingRoute({latitude:10,longitude:106},[{id:2},{id:1}]);
  assert.deepEqual(result.points,[[10,106],[10.1,106.2],[10.2,106.3]]);
  assert.deepEqual(result.stops.map(p=>p.poiId),[1,2]); assert.equal(result.distanceMeters,1234);
  assert.equal(result.durationSeconds,777); assert.equal(result.attribution,'ORS fixture');
});
test('ORS failure shows backend failure and does not fabricate a straight line', async t => {
  const fetch=t.mock.method(globalThis,'fetch',async ()=>Response.json({code:'routing_quota_exceeded',detail:'Dịch vụ tuyến đi bộ đã hết quota.'},{status:503}));
  await assert.rejects(createWalkingRoute({latitude:0,longitude:0},[{id:1}]),error=>error instanceof ApiError && error.problem.code==='routing_quota_exceeded');
  assert.equal(fetch.mock.callCount(),1);
});
