import { test, expect } from '@playwright/test';
import { codes, open, pois, select, setup } from './fixtures.mjs';

// Click real Leaflet pixels at zoom 15, relative to the Cathedral's marker anchor.
// No application test API: this exercises the same map click as a visitor.
async function choosePoint(page, latitude, longitude, zoom=15) {
  await page.getByRole('button',{name:'Chọn vị trí',exact:true}).click();
  const marker=await page.getByTitle('Nhà thờ Đức Bà',{exact:true}).boundingBox();
  const project=(lat,lng)=>{
    const sin=Math.sin(lat*Math.PI/180),scale=256*2**zoom;
    return {x:(lng+180)/360*scale,y:(.5-Math.log((1+sin)/(1-sin))/(4*Math.PI))*scale};
  };
  const origin=project(pois[0].latitude,pois[0].longitude),target=project(latitude,longitude);
  await page.mouse.click(marker.x+17+target.x-origin.x,marker.y+38+target.y-origin.y);
  await expect(page.locator('.map-instruction')).toHaveCount(0);
  await page.clock.runFor(500); // finish Leaflet panning before the next click
}

const mapNear={latitude:10.77948,longitude:106.699}; // 36m south; clear of the marker icon
const mapFar={latitude:10.7738,longitude:106.699}; // more than 500m from every fixture POI

test('existing map/list use API data; all 15 languages keep matching transcript and audio',async({page})=>{
  const world=await setup(page);await open(page);await expect(page.locator('.poi-list .poi-row')).toHaveCount(8);
  await expect(page.locator('.leaflet-marker-icon.poi-marker-wrap')).toHaveCount(8);await select(page);
  const languages=page.getByLabel('Ngôn ngữ thuyết minh');await expect(languages.locator('option')).toHaveCount(15);
  for(const code of codes){await languages.selectOption(code);await expect(page.locator('.transcript p')).toHaveText(`Fixture 1 - ${code}`);
    await expect(page.getByTestId('narration-audio')).toHaveAttribute('src',new RegExp(`/1/${code}\\.wav$`));}
  expect(world.posts).toHaveLength(0);
});
test('paused/fallback stays readable and repeated play/refresh does not POST in a loop',async({page})=>{
  const world=await setup(page);await open(page);await select(page,'Bưu điện');
  await expect(page.locator('.transcript p')).toHaveText('Fixture nguồn 2');await expect.poll(()=>world.posts.length).toBe(1);
  await page.getByLabel('Ngôn ngữ thuyết minh').selectOption('en');await expect(page.locator('.fallback-note')).toContainText('vi');
  await expect(page.locator('.detail-card')).toContainText('Nội dung hiện chưa sẵn sàng');
  await page.getByRole('button',{name:'Phát',exact:true}).click();await page.getByRole('button',{name:'Kiểm tra lại nội dung'}).click();
  expect(world.posts).toHaveLength(1);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
});
test('rapid language and POI changes cannot be overwritten by old responses',async({page})=>{
  await setup(page,{readDelay:{'1:ja':400}});await open(page);await select(page);
  await page.getByLabel('Ngôn ngữ thuyết minh').selectOption('ja');await page.getByLabel('Ngôn ngữ thuyết minh').selectOption('en');
  await expect(page.locator('.transcript p')).toHaveText('Fixture 1 - en');await page.waitForTimeout(500);
  await expect(page.locator('.transcript p')).toHaveText('Fixture 1 - en');
  await page.getByRole('button',{name:/Quay lại danh sách/}).click();await select(page,'Bưu điện');
  await expect(page.locator('.transcript p')).toHaveText('Fixture nguồn 2');
});
test('route preview displays backend order, geometry, metrics and ORS failure without fake fallback',async({page})=>{
  const world=await setup(page);await open(page);await page.locator('.places-actions').getByRole('button',{name:'Vị trí của tôi',exact:true}).click();
  await page.getByRole('button',{name:'Tạo lộ trình',exact:true}).click();
  await page.locator('.route-poi-picker').getByRole('button',{name:/Bưu điện/}).click();
  await page.locator('.route-poi-picker').getByRole('button',{name:/Nhà thờ/}).click();
  await page.getByRole('button',{name:'Tạo lộ trình (2)',exact:true}).click();
  await expect(page.locator('.route-result')).toContainText('1.2 km');await expect(page.locator('.route-result')).toContainText('13 phút');
  await expect(page.locator('.route-stop-name').first()).toHaveText('Nhà thờ Đức Bà');
  await expect(page.locator('path.leaflet-interactive')).toHaveCount(1);
  expect(world.routes[0]).toEqual({startLatitude:10.7798,startLongitude:106.699,poiIds:[2,1]});
  world.routeError=true;await page.getByRole('button',{name:'Cập nhật lộ trình'}).click();
  await expect(page.locator('.route-error')).toContainText('Fixture ORS không phản hồi');await expect(page.locator('.route-result')).toHaveCount(0);
});
test('API failure is visible and mobile layout remains usable without horizontal overflow',async({page},testInfo)=>{
  const world=await setup(page,{listError:true});await page.setViewportSize({width:390,height:844});await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('Không tải được danh sách');world.listError=false;
  await page.getByRole('button',{name:'Thử lại',exact:true}).click();await expect(page.locator('.poi-list .poi-row')).toHaveCount(8);
  await select(page);await expect(page.getByLabel('Ngôn ngữ thuyết minh')).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:testInfo.outputPath('mobile.png'),fullPage:true});
});
test('GPS permission denial leaves map and manual content available',async({page})=>{
  await setup(page);await open(page);await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();
  await page.evaluate(()=>window.__gpsError(1));await expect(page.locator('.notice')).toContainText('chưa cấp quyền');
  await expect(page.getByRole('button',{name:'Bắt đầu thuyết minh'})).toBeVisible();await select(page);
  await expect(page.locator('.transcript p')).toHaveText('Fixture 1 - vi');
});
test('simulated GPS cancels exit before dwell, prefetches once and plays one source audio per visit',async({page})=>{
  const world=await setup(page);await page.clock.install();await open(page);
  await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();await page.evaluate(()=>window.__gps());
  await page.clock.runFor(1000);await expect.poll(()=>world.posts.length).toBe(1);
  await page.clock.runFor(1000);await page.evaluate(()=>window.__gps(10.781,106.699));await page.clock.runFor(2500);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.evaluate(()=>window.__gps());await page.clock.runFor(3250);
  await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(1);
  expect((await page.evaluate(()=>window.__plays))[0]).toContain('/1/vi.wav');
  await page.clock.runFor(1000);await page.evaluate(()=>window.__gps());await page.clock.runFor(3500);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(1);expect(world.posts.filter(p=>p.path.endsWith('prepare-nearby'))).toHaveLength(1);
  await page.getByRole('button',{name:'Dừng thuyết minh'}).click();expect(await page.evaluate(()=>window.__cleared)).toBeGreaterThan(0);
});
test('stale/inaccurate GPS never autoplays and browser autoplay denial allows a manual retry',async({page})=>{
  await setup(page);await page.clock.install();await open(page);await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();
  await page.evaluate(()=>window.__gps(10.7798,106.699,100));await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.evaluate(()=>window.__gps(10.7798,106.699,5,20000));await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.evaluate(()=>{window.__blockAudio=true;window.__gps();});await page.clock.runFor(3500);
  await expect(page.locator('.audio-note')).toContainText('Trình duyệt chặn tự phát');
  await page.evaluate(()=>{window.__blockAudio=false;});await page.getByRole('button',{name:'Phát',exact:true}).click();
  await expect(page.getByRole('button',{name:'Tạm dừng',exact:true})).toBeVisible();
});
test('mock-only 202 polling stops on selection change and late source audio does not override a foreign choice',async({page})=>{
  const world=await setup(page,{ready:false,accepted:true});await page.clock.install();await open(page);
  await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();await page.evaluate(()=>window.__gps());await page.clock.runFor(1000);
  await select(page);await page.getByLabel('Ngôn ngữ thuyết minh').selectOption('en');world.ready=true;
  await page.clock.runFor(4000);expect((await page.evaluate(()=>window.__plays)).filter(url=>url.includes('/vi.wav'))).toHaveLength(0);
  await page.getByRole('button',{name:'Dừng thuyết minh'}).click();const polls=world.polls;
  await page.clock.runFor(10000);expect(world.polls).toBe(polls);
});

test('429 stops repeated clicks until Retry-After and permits a later explicit retry',async({page})=>{
  const world=await setup(page,{rateLimited:true});await page.clock.install();await open(page);await select(page,'Bưu điện');
  await expect(page.locator('.detail-card')).toContainText('Đang giới hạn yêu cầu');
  await page.getByRole('button',{name:'Phát',exact:true}).click();await page.getByRole('button',{name:'Kiểm tra lại nội dung'}).click();
  expect(world.posts).toHaveLength(1);
  await page.clock.runFor(61000);world.rateLimited=false;await page.getByRole('button',{name:'Kiểm tra lại nội dung'}).click();
  await expect.poll(()=>world.posts.length).toBe(2);await expect(page.locator('.detail-card')).toContainText('Nội dung hiện chưa sẵn sàng');
});

test('map location drives 500m preparation, 50m dwell and a single audio without requesting GPS',async({page})=>{
  const world=await setup(page);await page.clock.install();await open(page);
  await choosePoint(page,mapFar.latitude,mapFar.longitude);
  await expect(page.getByRole('button',{name:'Dừng thuyết minh'})).toBeVisible();await page.clock.runFor(4000);
  expect(world.posts).toHaveLength(0);expect(await page.evaluate(()=>window.__gpsCallbacks.length)).toBe(0);
  await choosePoint(page,10.7798,106.701);await expect.poll(()=>world.posts.length).toBe(1);
  const preparedPoint=world.posts[0].body;
  expect(preparedPoint.latitude).toBeCloseTo(10.7798,4);expect(preparedPoint.longitude).toBeCloseTo(106.701,4);
  await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await page.clock.runFor(2000);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.clock.runFor(1250);await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(1);
  expect((await page.evaluate(()=>window.__plays))[0]).toContain('/1/vi.wav');
  await page.clock.runFor(60_000);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(1);expect(world.posts).toHaveLength(1);
  await expect(page.locator('.integration-note').filter({hasText:'Dùng vị trí đã chọn'})).toBeVisible();
  await page.getByRole('button',{name:'Dừng thuyết minh'}).click();
  await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();await page.clock.runFor(3500);
  await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(2); // same stationary map point, new visit
});

test('map exit before dwell cancels the count; re-entry needs a full dwell and marker clicks keep the visitor point',async({page})=>{
  const world=await setup(page);await page.clock.install();await open(page);
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await expect(page.getByRole('button',{name:'Dừng thuyết minh'})).toBeVisible();
  await page.clock.runFor(1500);await choosePoint(page,10.7798,106.701);await page.clock.runFor(4000);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.getByRole('button',{name:'Chọn vị trí',exact:true}).click();
  const before=await page.locator('.location-marker').getAttribute('style');
  await page.getByTitle('Nhà thờ Đức Bà',{exact:true}).click();
  await expect(page.locator('.map-instruction')).toBeVisible();
  expect(await page.locator('.location-marker').getAttribute('style')).toBe(before);
  await page.getByRole('button',{name:/Quay lại danh sách/}).click();
  await page.getByRole('button',{name:'Close',exact:true}).click();
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await page.clock.runFor(2000);
  expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.clock.runFor(1250);await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(1);
  expect(world.posts.filter(post=>post.path.endsWith('prepare-nearby'))).toHaveLength(1);
});

test('GPS callbacks cannot replace a map point; switching back discards the point and waits for a new GPS fix',async({page})=>{
  await setup(page);await page.clock.install();await open(page);
  await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();await page.evaluate(()=>window.__gps());
  await page.clock.runFor(1000);await choosePoint(page,mapFar.latitude,mapFar.longitude);
  await page.evaluate(()=>window.__gpsCallbacks[0]({coords:{latitude:10.7798,longitude:106.699,accuracy:5},timestamp:Date.now()}));
  await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await expect(page.locator('.map-location')).toContainText('Vị trí đã chọn');
  await page.locator('.places-actions').getByRole('button',{name:'Vị trí của tôi',exact:true}).click();
  await expect(page.locator('.location-marker')).toHaveCount(0);
  await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await page.evaluate(()=>window.__gps());await page.clock.runFor(3250);
  await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(1);
  await expect(page.locator('.map-location')).toContainText('Vị trí hiện tại');
});

test('a late one-shot GPS result is ignored after map selection and map points are used as route origins',async({page})=>{
  const world=await setup(page);await page.clock.install();await open(page);
  await page.evaluate(()=>{window.__holdLocate=true;});
  await page.locator('.places-actions').getByRole('button',{name:'Vị trí của tôi',exact:true}).click();
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await page.evaluate(()=>window.__resolveLocate());
  await expect(page.locator('.map-location')).toContainText('Vị trí đã chọn');
  await page.getByRole('button',{name:'Tạo lộ trình',exact:true}).click();
  for(const name of [/Nhà thờ/,/Bưu điện/])await page.locator('.route-poi-picker').getByRole('button',{name}).click();
  await page.getByRole('button',{name:'Tạo lộ trình (2)',exact:true}).click();await expect.poll(()=>world.routes.length).toBe(1);
  expect(Math.abs(world.routes[0].startLatitude-mapNear.latitude)).toBeLessThan(.00008); // allow pixel rounding (<9m)
  expect(Math.abs(world.routes[0].startLatitude-pois[0].latitude)).toBeGreaterThan(.0002);
  expect(Math.abs(world.routes[0].startLongitude-mapNear.longitude)).toBeLessThan(.00008);
});

test('a late nearby/source response cannot play after rapid map moves',async({page})=>{
  const world=await setup(page,{nearbyDelay:1200,readDelay:{'1:vi':1200}});await page.clock.install();await open(page);
  await choosePoint(page,mapNear.latitude,mapNear.longitude);
  await page.clock.runFor(3250);await expect.poll(()=>world.reads.includes('1:vi')).toBe(true);
  await choosePoint(page,mapFar.latitude,mapFar.longitude);await page.waitForTimeout(1500);
  await page.clock.runFor(4000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await page.clock.runFor(3500);
  await expect.poll(()=>page.evaluate(()=>window.__plays.length)).toBe(1);
  expect(world.posts.filter(post=>post.path.endsWith('prepare-nearby')).length).toBeLessThanOrEqual(2);
});

test('a chosen map point works after GPS denial and autoplay denial still permits manual play',async({page})=>{
  await setup(page);await page.clock.install();await open(page);
  await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();await page.evaluate(()=>window.__gpsError(1));
  await choosePoint(page,mapNear.latitude,mapNear.longitude);await page.evaluate(()=>{window.__blockAudio=true;});
  await page.clock.runFor(3500);
  await expect(page.locator('.audio-note')).toContainText('Trình duyệt chặn tự phát');
  await page.clock.runFor(30_000);expect(await page.evaluate(()=>window.__plays)).toHaveLength(1);
  await page.evaluate(()=>{window.__blockAudio=false;});await page.getByRole('button',{name:'Phát',exact:true}).click();
  await expect(page.getByRole('button',{name:'Tạm dừng',exact:true})).toBeVisible();
});

test('missing map-nearby content stays paused without repeated POSTs or playback',async({page})=>{
  const world=await setup(page);await page.clock.install();await open(page);
  await choosePoint(page,10.7798,106.701);
  await page.locator('.leaflet-map').press('Equal');await page.clock.runFor(500);
  await page.locator('.leaflet-map').press('Equal');await page.clock.runFor(500);
  await choosePoint(page,pois[1].latitude-.00032,pois[1].longitude,17);
  await page.clock.runFor(3500);
  await select(page,'Bưu điện');await expect(page.locator('.detail-card')).toContainText('Nội dung hiện chưa sẵn sàng');
  await page.clock.runFor(30_000);
  expect(world.posts).toHaveLength(2);expect(await page.evaluate(()=>window.__plays)).toHaveLength(0);
});
