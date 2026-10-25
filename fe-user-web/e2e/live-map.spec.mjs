import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';

// Real API, SQL content, settings, audio and media playback. No provider mocks.
test.use({ viewport: { width: 1280, height: 900 } });
test.afterEach(async ({ page }, info) => {
  if (page.__positionEvidence) await page.__positionEvidence.finish(info);
});
const near = { latitude: 10.77948, longitude: 106.699 };
const middle = { latitude: 10.7798, longitude: 106.701 };
const zoo = { latitude: 10.7857, longitude: 106.705 };

async function open(page) {
  const evidence = { events: [], posts: [], responses: [], pageErrors: [] };
  const captures = [];
  page.on('console', message => {
    if (message.text().startsWith('[tour] ')) evidence.events.push(JSON.parse(message.text().slice(7)));
  });
  page.on('pageerror', error => evidence.pageErrors.push(error.message));
  page.on('request', request => {
    if (request.method() === 'POST' && new URL(request.url()).pathname.startsWith('/api/'))
      evidence.posts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() });
  });
  page.on('response', response => {
    if (response.request().method() === 'POST' && response.url().includes('prepare-'))
      captures.push(response.json().then(body => evidence.responses.push({ status: response.status(), items: (body.items ?? [body]).map(item => ({
        poiId: item.poiId, status: item.status, errorCode: item.errorCode, jobId: item.jobId,
        distanceMeters: item.distanceMeters, audioReady: item.audioReady,
      })) })));
  });
  await page.addInitScript(() => {
    window.__mapLive = { plays: [], deny: false };
    const nativePlay = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      const call = { at: performance.now(), path: new URL(this.src).pathname, success: false };
      window.__mapLive.plays.push(call);
      const result = window.__mapLive.deny ? Promise.reject(new DOMException('Browser-policy test', 'NotAllowedError')) : nativePlay.call(this);
      return result.then(() => { call.success = true; }).catch(error => { call.error = error.name; throw error; });
    };
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'VI', exact: true }).click();
  await expect(page.locator('.poi-list .poi-row')).toHaveCount(8, { timeout: 30_000 });
  await expect(page.locator('.narration-action')).toBeEnabled();
  evidence.settings = await page.evaluate(() => fetch('/api/content-preparations/settings').then(r => r.json()));
  expect(evidence.settings).toMatchObject({ preparationRadiusMeters: 500, playbackRadiusMeters: 50, dwellSeconds: 3 });
  evidence.finish = async info => {
    await Promise.all(captures);
    evidence.plays = await page.evaluate(() => window.__mapLive.plays);
    const { finish: _finish, ...saved } = evidence;
    void _finish;
    await writeFile(info.outputPath('position-evidence.json'), JSON.stringify(saved, null, 2));
    expect(evidence.pageErrors).toEqual([]);
  };
  page.__positionEvidence = evidence;
  return evidence;
}

async function choose(page, point) {
  await page.getByRole('button', { name: 'Chọn vị trí', exact: true }).click();
  const marker = await page.getByTitle('Nhà thờ Đức Bà', { exact: true }).boundingBox();
  const project = (lat, lng) => {
    const sin = Math.sin(lat * Math.PI / 180), scale = 256 * 2 ** 15;
    return { x: (lng + 180) / 360 * scale, y: (.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale };
  };
  const origin = project(10.7798, 106.699), target = project(point.latitude, point.longitude);
  await page.mouse.click(marker.x + 17 + target.x - origin.x, marker.y + 38 + target.y - origin.y);
  await expect(page.locator('.map-instruction')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Dừng thuyết minh' })).toBeVisible();
  await page.waitForTimeout(350); // Leaflet pan animation; dwell uses the real browser clock.
}

const plays = page => page.evaluate(() => window.__mapLive.plays);
async function playing(page) {
  await expect.poll(() => page.locator('audio').evaluate(a => !a.paused && a.currentTime > 0 && Number.isFinite(a.duration)), { timeout: 20_000 }).toBe(true);
}

test('LIVE map: direct Cathedral selection starts dwell and real playback once, even while generation is paused', async ({ page }) => {
  const evidence = await open(page);
  expect(await page.locator('.detail-card').count()).toBe(0);
  await choose(page, near);
  await playing(page);
  const start = evidence.events.find(e => e.event === 'dwell-start' && e.poiId === 1);
  const play = evidence.events.find(e => e.event === 'audio-play' && e.poiId === 1);
  expect(start.distance).toBeLessThan(50);
  expect(play.at - start.at).toBeGreaterThanOrEqual(3000);
  expect((await plays(page))[0].path).toContain('/poi-1/vi/');
  await expect.poll(() => evidence.responses.length).toBe(1);
  expect(evidence.responses[0].items.find(i => i.poiId === 1)).toMatchObject({ status: 'Ready', audioReady: true });
  expect(evidence.responses[0].items.some(i => i.errorCode === 'GenerationPaused')).toBe(true);
  await page.waitForTimeout(11_000);
  expect(await plays(page)).toHaveLength(1);expect(evidence.posts).toHaveLength(1);
  await page.getByRole('button', { name: 'Dừng thuyết minh' }).click();
});

test('LIVE map: leaving before three seconds cancels dwell without a play call', async ({ page }) => {
  const evidence = await open(page);
  await choose(page, near);await page.waitForTimeout(700);
  await choose(page, middle);await page.waitForTimeout(3400);
  expect(await plays(page)).toHaveLength(0);
  const start = evidence.events.find(e => e.event === 'dwell-start' && e.poiId === 1);
  const cancel = evidence.events.find(e => e.event === 'dwell-cancel' && e.ids.includes(1));
  expect(cancel.at - start.at).toBeLessThan(3000);
});

test('LIVE map: Zoo preparation is visibly paused; a new Cathedral point still prepares and plays Ready audio', async ({ page }) => {
  const evidence = await open(page);
  await page.locator('.leaflet-map').press('ArrowUp');await page.waitForTimeout(250);
  await page.locator('.leaflet-map').press('ArrowUp');await page.waitForTimeout(250);
  await choose(page, zoo);
  await expect(page.getByRole('status').filter({ hasText: /^Thảo Cầm Viên.*tạm dừng/ })).toBeVisible();
  await expect.poll(() => evidence.responses.length).toBe(1);
  const item = evidence.responses[0].items.find(i => i.poiId === 8);
  expect(evidence.responses[0].status).toBe(503);
  expect(item).toMatchObject({ status: 'Unavailable', errorCode: 'GenerationPaused', jobId: null, audioReady: false });
  expect(item.distanceMeters).toBeGreaterThan(50);expect(item.distanceMeters).toBeLessThan(500);
  await page.waitForTimeout(11_000);expect(evidence.posts).toHaveLength(1);expect(await plays(page)).toHaveLength(0);
  await page.locator('.poi-list').getByRole('button', { name: /Thảo Cầm Viên/ }).click();
  await expect(page.locator('.transcript p')).not.toBeEmpty();
  await choose(page, near);await playing(page);
  await expect.poll(() => evidence.responses.length).toBe(2);
  expect(evidence.responses[1].items.find(i => i.poiId === 1).status).toBe('Ready');
  expect(await plays(page)).toHaveLength(1);
  await page.getByRole('button', { name: 'Dừng thuyết minh' }).click();
});

test('LIVE map: a forced browser autoplay denial exposes manual play; GPS and map transitions retain the active source', async ({ page, context }) => {
  const evidence = await open(page);
  await page.evaluate(() => { window.__mapLive.deny = true; });
  await choose(page, near);
  await expect(page.locator('.audio-player .audio-note')).toContainText('Trình duyệt chặn tự phát');
  await expect(page.getByRole('button', { name: 'Phát', exact: true })).toBeVisible();
  const failed = await plays(page);expect(failed).toHaveLength(1);expect(failed[0].error).toBe('NotAllowedError');
  await page.waitForTimeout(4000);expect(await plays(page)).toHaveLength(1);
  const result = evidence.events.find(e => e.event === 'autoplay-result');expect(result.heard).toEqual([]);
  await page.evaluate(() => { window.__mapLive.deny = false; });
  await page.getByRole('button', { name: 'Phát', exact: true }).click();await playing(page);
  await context.grantPermissions(['geolocation']);await context.setGeolocation({ ...zoo, accuracy: 5 });
  await page.locator('.places-actions').getByRole('button', { name: 'Vị trí của tôi', exact: true }).click();
  await expect(page.locator('.map-location')).toContainText('Vị trí hiện tại');
  await choose(page, near);
  await context.setGeolocation({ latitude: 10.79, longitude: 106.71, accuracy: 5 });
  await page.waitForTimeout(3500);
  await expect(page.locator('.map-location')).toContainText('Vị trí đã chọn');
  expect((await plays(page)).filter(call => call.success)).toHaveLength(1); // manual success was heard; no replay
  evidence.browserPolicyForced = true;evidence.gpsEmulated = true;
});
