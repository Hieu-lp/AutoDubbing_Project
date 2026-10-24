import test from 'node:test';
import assert from 'node:assert/strict';
import { distanceMeters, gpsPolicy, ProximityTracker, trustedFix } from '../src/tour/proximity.ts';
const now = 100_000_000;
const fix = { latitude: 0, longitude: 0, accuracy: 5, timestamp: now };
const settings = { preparationRadiusMeters: 500, playbackRadiusMeters: 50, dwellSeconds: 3, pollAfterSeconds: 3 };
const poi = (id, latitude = 0) => ({ id, latitude, longitude: 0, name: 'Fixture', originalLanguage: 'vi', proximityRadius: 999 });

test('fresh and accurate fixes only; stale, inaccurate, absent, invalid and future fixes fail', () => {
  assert.ok(trustedFix(fix,now));
  for (const input of [null,{...fix,accuracy:51},{...fix,timestamp:now-gpsPolicy.maxAgeMs-1},
    {...fix,timestamp:now+5000},{...fix,latitude:NaN},{...fix,latitude:91},{...fix,accuracy:undefined}])
    assert.equal(trustedFix(input,now),false);
});
test('500m preparation is independent of 50m playback and stored POI radii', () => {
  const tracker = new ProximityTracker();
  const result = tracker.update([poi(1),poi(2,.003),poi(3,.006)],fix,settings,now,0);
  assert.deepEqual(result.nearby.map(p=>p.id),[1,2]); assert.deepEqual(result.eligible,[]);
  assert.ok(distanceMeters(fix,poi(2,.003)) > 50);
});
test('continuous dwell, cancellation on exit and fresh count on re-entry', () => {
  const tracker = new ProximityTracker();
  tracker.update([poi(1)],fix,settings,now,0);
  assert.equal(tracker.update([poi(1)],fix,settings,now+2999,2999).eligible.length,0);
  tracker.update([poi(1)],{...fix,latitude:.001,timestamp:now+2000},settings,now+2000,2000);
  assert.equal(tracker.update([poi(1)],{...fix,timestamp:now+3000},settings,now+3000,3000).eligible.length,0);
  assert.equal(tracker.update([poi(1)],{...fix,timestamp:now+6000},settings,now+6000,6000).eligible[0].id,1);
});
test('lost or stale fix resets dwell; nearest then ID determines one eligible candidate', () => {
  const tracker = new ProximityTracker(); const pois=[poi(3,.0001),poi(2),poi(1)];
  tracker.update(pois,fix,settings,now,0);
  assert.deepEqual(tracker.update(pois,fix,settings,now+3000,3000).eligible.map(p=>p.id),[1,2,3]);
  tracker.update(pois,null,settings,now+4000,4000);
  assert.equal(tracker.update(pois,{...fix,timestamp:now+5000},settings,now+5000,5000).eligible.length,0);
  assert.equal(tracker.update(pois,fix,settings,now+11000,11000).eligible.length,0);
});
test('backend settings govern thresholds instead of hardcoded 50m/3s', () => {
  const tracker=new ProximityTracker(); const config={...settings,playbackRadiusMeters:20,dwellSeconds:5};
  const pois=[poi(1,.0001),poi(2,.0003)];
  tracker.update(pois,fix,config,now,0);
  assert.equal(tracker.update(pois,fix,config,now+4000,4000).eligible.length,0);
  assert.deepEqual(tracker.update(pois,fix,config,now+5000,5000).eligible.map(p=>p.id),[1]);
});
