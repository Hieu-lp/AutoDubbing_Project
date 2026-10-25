import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';

test.describe.configure({mode:'default'});
async function openLive(page) {
  await page.goto('/');await page.getByRole('button',{name:'VI',exact:true}).click();
  await expect(page.locator('.poi-list .poi-row')).toHaveCount(8,{timeout:30000});
}
const select=(page,name)=>page.locator('.poi-list').getByRole('button',{name:new RegExp(name)}).click();

test('LIVE: 8 SQL POIs and Cathedral 15 stored languages with readable WAV metadata',async({page},info)=>{
  test.setTimeout(240000);
  const posts=[];page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/api/'))posts.push(r.url());});
  await openLive(page);await expect(page.locator('.leaflet-marker-icon.poi-marker-wrap')).toHaveCount(8);
  await page.getByTitle('Nhà thờ Đức Bà',{exact:true}).click();const languages=page.getByLabel('Ngôn ngữ thuyết minh');
  await expect(languages.locator('option')).toHaveCount(15);const codes=await languages.locator('option').evaluateAll(options=>options.map(o=>o.value));
  const samples=[];
  for(const code of codes){
    await languages.selectOption(code);
    await expect(page.getByTestId('narration-audio')).toHaveAttribute('src',new RegExp(`/1/|/poi-1/${code}/`),{timeout:20000});
    await page.waitForFunction(code=>{const a=document.querySelector('audio');return a?.src.includes(`/poi-1/${code}/`)&&Number.isFinite(a.duration)&&a.duration>0;},code,{timeout:30000});
    samples.push(await page.getByTestId('narration-audio').evaluate(a=>({url:a.currentSrc||a.src,durationSeconds:a.duration})));
    await page.getByTestId('narration-audio').evaluate(a=>a.pause());
    await expect(page.locator('.fallback-note')).toHaveCount(0);
  }
  await languages.selectOption('vi');
  const play=page.getByRole('button',{name:'Phát',exact:true});if(await play.isVisible())await play.click();
  await expect(page.getByRole('button',{name:'Tạm dừng',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Tạm dừng',exact:true}).click();
  expect(posts).toHaveLength(0);await writeFile(info.outputPath('audio-live.json'),JSON.stringify(samples,null,2));
  await page.screenshot({path:info.outputPath('desktop-live.png'),fullPage:true});
});
test('LIVE: missing audio remains readable with GenerationPaused and explicit fallback',async({page},info)=>{
  const responses=[];page.on('response',r=>{if(r.request().method()==='POST'&&r.url().includes('prepare-content'))responses.push(r);});
  await openLive(page);await select(page,'Bưu điện');
  await expect(page.locator('.detail-card')).toContainText('Nội dung hiện chưa sẵn sàng');
  await page.getByLabel('Ngôn ngữ thuyết minh').selectOption('en');await expect(page.locator('.fallback-note')).toContainText('vi');
  await expect(page.locator('.transcript p')).not.toBeEmpty();expect(responses.length).toBe(1);
  expect(responses[0].status()).toBe(503);const body=await responses[0].json();expect(body.errorCode).toBe('GenerationPaused');expect(body.jobId).toBeNull();
  await writeFile(info.outputPath('paused-live.json'),JSON.stringify(body,null,2));
});
test('LIVE: walking preview through backend ORS or its explicit provider error',async({page},info)=>{
  await openLive(page);await page.getByRole('button',{name:'Chọn vị trí',exact:true}).click();
  await page.locator('.leaflet-map').click({position:{x:60,y:180}});
  await expect(page.locator('.notice')).toContainText('làm vị trí du khách');
  await page.getByRole('button',{name:'Tạo lộ trình',exact:true}).click();
  await page.locator('.route-poi-picker').getByRole('button',{name:/Nhà thờ/}).click();
  await page.locator('.route-poi-picker').getByRole('button',{name:/Bưu điện/}).click();
  const response=page.waitForResponse(r=>r.url().endsWith('/api/routes/preview')&&r.request().method()==='POST',{timeout:40000});
  await page.getByRole('button',{name:'Tạo lộ trình (2)',exact:true}).click();
  const result=await response;const body=await result.json();
  await writeFile(info.outputPath('route-live.json'),JSON.stringify({http:result.status(),body},null,2));
  if(result.ok()){
    expect(body.distanceType).toBe('walking');expect(body.geometry.type).toBe('LineString');expect(body.geometry.coordinates.length).toBeGreaterThan(2);
    await expect(page.locator('.route-result')).toContainText('Đã tạo tuyến đi bộ');await expect(page.locator('path.leaflet-interactive')).toHaveCount(1);
  } else {await expect(page.locator('.route-error')).toBeVisible();await expect(page.locator('.route-result')).toHaveCount(0);}
  await page.screenshot({path:info.outputPath('route-live.png'),fullPage:true});
});
test('LIVE API + simulated browser geolocation: one source preparation and one source audio after dwell',async({page,context},info)=>{
  await context.grantPermissions(['geolocation']);await context.setGeolocation({latitude:10.7798,longitude:106.699,accuracy:5});
  const requests=[];page.on('request',r=>{if(r.method()==='POST'&&r.url().includes('/api/'))requests.push({url:r.url(),body:r.postDataJSON()});});
  await openLive(page);await page.getByRole('button',{name:'Bắt đầu thuyết minh'}).click();
  await expect(page.getByRole('button',{name:'Tạm dừng',exact:true})).toBeVisible({timeout:30000});
  await expect(page.getByTestId('narration-audio')).toHaveAttribute('src',/poi-1\/vi\//);
  expect(requests.filter(r=>r.url.endsWith('prepare-nearby'))).toHaveLength(1);
  expect(requests.filter(r=>r.url.endsWith('prepare-content'))).toHaveLength(0);
  await page.getByRole('button',{name:'Dừng thuyết minh'}).click();
  await writeFile(info.outputPath('gps-simulated-live.json'),JSON.stringify({simulated:true,requests},null,2));
});
test('LIVE: mobile layout and backend content remain usable',async({page},info)=>{
  await page.setViewportSize({width:390,height:844});await openLive(page);await select(page,'Nhà thờ');
  await expect(page.locator('.transcript p')).not.toBeEmpty();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:info.outputPath('mobile-live.png'),fullPage:true});
});
