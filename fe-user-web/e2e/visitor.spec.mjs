import { test, expect } from '@playwright/test';
import { codes, open, select, setup } from './fixtures.mjs';

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
