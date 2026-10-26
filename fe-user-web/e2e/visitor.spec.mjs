import { test, expect } from '@playwright/test';
import { codes, open, pois, prepared, select, setup, stored } from './fixtures.mjs';

async function openAdminContent(page, options) {
  const world = await setup(page, options);
  await page.addInitScript(() => {
    sessionStorage.setItem('voicemap.admin.access-token', 'fixture-content-token');
    localStorage.setItem('voicemap.admin.demo.narrations', JSON.stringify([{ poiId: 1, language: 'en', text: 'OLD LOCAL DRAFT', status: 'approved' }]));
  });
  await page.route('**/api/admin/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (/\/pois\/\d+\/(content|prepare-content)$/.test(path)) return route.fallback();
    if (path.endsWith('/contents')) return route.fulfill({ json: { translations: [], audios: [] } });
    if (route.request().method() !== 'GET') return route.abort();
    const data = path.endsWith('/auth/me') ? { id: 1, username: 'fixture', fullName: 'Fixture', status: 'Active' }
      : path.endsWith('/pois') ? pois.map(poi => ({ ...poi, status: poi.id === 8 ? 'Hidden' : 'Published' }))
        : { health: { api: 'online', database: 'online', pipeline: 'online' }, pipeline: { queued: 0, processing: 0, failed: 0, errorRatePercent: null, avgResponseMs: null, recentErrors: [] },
          usage: { activeUsers: null, autoPlayCount: null, manualPlayCount: null, listensByPoi: [], listensByLanguage: [], topRoute: null } };
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
  });
  await page.goto('/admin');
  await page.getByRole('button', { name: 'Bản dịch & audio', exact: true }).click();
  return { world, screen: page.getByRole('region', { name: 'Bản dịch và audio' }) };
}

test('admin content reads persisted audio, ignores local drafts and reloads without generation', async ({ page }) => {
  const { world, screen } = await openAdminContent(page);
  const text = screen.getByLabel(/Nội dung đã lưu/);
  await expect(text).toHaveValue('Fixture 1 - en');
  await expect(text).toHaveAttribute('readonly', '');
  await expect(screen.getByText('Hoàn tất', { exact: true })).toBeVisible();
  await expect(screen.getByRole('button', { name: /Chuẩn bị nội dung|Tạo audio còn thiếu/ })).toBeDisabled();
  await expect(screen.getByRole('button', { name: /Duyệt nội dung|Lưu chờ duyệt|Tạo bản demo/ })).toHaveCount(0);
  await screen.getByRole('button', { name: 'Phát audio đã lưu' }).click();
  await expect.poll(() => page.evaluate(() => window.__plays.at(-1))).toBe('https://audio.fixture.invalid/1/en.wav');
  await screen.getByRole('button', { name: 'Tạm dừng' }).click();
  await screen.getByRole('button', { name: 'Đọc lại dữ liệu' }).click();
  await page.reload();
  await page.getByRole('button', { name: 'Bản dịch & audio', exact: true }).click();
  await expect(text).toHaveValue('Fixture 1 - en');
  expect(world.posts).toEqual([]);
  await screen.getByRole('button', { name: /Thảo Cầm Viên/ }).click();
  await expect(screen).toContainText('Đang đọc POI ẩn bằng quyền Admin');
  await expect.poll(() => world.reads.some(key => key.startsWith('8:'))).toBe(true);
});

test('admin content continues text-only audio through the shared queue and reuses the saved result', async ({ page }) => {
  let completed = false;
  let polls = 0;
  let holdRead = false;
  let releaseRead;
  const blockedRead = new Promise(resolve => { releaseRead = resolve; });
  const posts = [];
  const textOnly = { ...stored(2, 'en'), audio: null };
  const { screen } = await openAdminContent(page);
  await page.route(/\/api\/(?:admin\/)?pois\/2\/(content\?|prepare-content)/, async route => {
    if (route.request().method() === 'GET') {
      const snapshot = completed ? stored(2, 'en') : textOnly;
      if (holdRead) await blockedRead;
      return route.fulfill({ json: snapshot }).catch(() => {});
    }
    posts.push(route.request().postDataJSON());
    return route.fulfill({ status: 202, headers: { 'Retry-After': '3' }, json: {
      ...prepared(2, 'en', 'Queued'), textReady: true, content: textOnly,
    } });
  });
  await page.route('**/api/content-preparations/fixture-job', route => {
    completed = ++polls >= 2;
    return route.fulfill({ json: { ...prepared(2, 'en', completed ? 'Ready' : 'Processing'),
      jobId: 'fixture-job', textReady: true, content: completed ? stored(2, 'en') : textOnly } });
  });
  await screen.getByRole('button', { name: /Bưu điện Trung tâm/ }).click();
  await expect(screen.getByLabel(/Nội dung đã lưu/)).toHaveValue(textOnly.text);
  await screen.getByRole('button', { name: 'Tạo audio còn thiếu' }).click();
  await expect(screen.getByText('Đang chờ', { exact: true })).toBeVisible();
  await expect(screen.getByText('Đang xử lý', { exact: true })).toBeVisible({ timeout: 8000 });
  holdRead = true;
  const readStarted = page.waitForRequest(request => request.url().includes('/pois/2/content?'));
  await screen.getByRole('button', { name: 'Đọc lại dữ liệu' }).click();
  await readStarted;
  await expect(screen.getByText('Hoàn tất', { exact: true })).toBeVisible({ timeout: 8000 });
  releaseRead();
  holdRead = false;
  await screen.getByRole('button', { name: 'Phát audio đã lưu' }).click();
  await expect.poll(() => page.evaluate(() => window.__plays.at(-1))).toBe('https://audio.fixture.invalid/2/en.wav');
  await page.reload();
  await page.getByRole('button', { name: 'Bản dịch & audio', exact: true }).click();
  await screen.getByRole('button', { name: /Bưu điện Trung tâm/ }).click();
  await expect(screen.getByText('Hoàn tất', { exact: true })).toBeVisible();
  expect(posts).toEqual([{ language: 'en' }]);
});

for (const [code, status] of [['SupabaseObjectMismatch', 'Unavailable'], ['DailyQuotaExceeded', 'QuotaExceeded']]) {
  test(`admin content shows fallback and ${code} without automatic retries`, async ({ page }) => {
    const { world, screen } = await openAdminContent(page, { ready: false });
    await page.route('**/api/admin/pois/1/prepare-content', route => {
      world.posts.push(route.request().postDataJSON());
      return route.fulfill({ status: 503, json: { ...prepared(1, 'en', status), errorCode: code,
        retryAt: '2099-01-01T00:00:00Z', requiresOperatorAction: status === 'QuotaExceeded' } });
    });
    await expect(screen).toContainText('Đang hiển thị bản gốc (vi); chưa có bản dịch (en)');
    await expect(screen.getByRole('button', { name: 'Phát audio đã lưu' })).toBeDisabled();
    expect(world.posts).toEqual([]);
    await screen.getByRole('button', { name: 'Chuẩn bị nội dung & audio' }).click();
    await expect(screen).toContainText(code);
    await expect(screen).toContainText('Có thể thử lại từ');
    await screen.getByRole('button', { name: 'Đọc lại dữ liệu' }).click();
    await screen.getByLabel('Ngôn ngữ đích').selectOption('ja');
    await screen.getByLabel('Ngôn ngữ đích').selectOption('en');
    expect(world.posts).toHaveLength(1);
    await expect(screen.getByLabel(/Nội dung đã lưu/)).not.toHaveValue('OLD LOCAL DRAFT');
  });
}

test('admin content rejects unsafe audio URLs and ignores late responses after changing selection', async ({ page }) => {
  const { world, screen } = await openAdminContent(page);
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  await page.route(/\/api\/(?:admin\/)?pois\/2\/content\?/, async route => {
    await gate;
    return route.fulfill({ json: stored(2, 'en') }).catch(() => {});
  });
  await page.route(/\/api\/(?:admin\/)?pois\/1\/content\?language=ja/, route => route.fulfill({ json: {
    ...stored(1, 'ja'), audio: { id: 1, language: 'ja', url: 'http://audio.fixture.invalid/unsafe.wav' },
  } }));
  await screen.getByRole('button', { name: /Bưu điện Trung tâm/ }).click();
  await screen.getByRole('button', { name: /Nhà thờ Đức Bà/ }).click();
  release();
  await screen.getByLabel('Ngôn ngữ đích').selectOption('ja');
  await expect(screen).toContainText('URL audio không hợp lệ');
  await expect(screen.getByRole('button', { name: 'Phát audio đã lưu' })).toBeDisabled();
  await expect(screen.getByLabel(/Nội dung đã lưu/)).toHaveValue('Fixture 1 - ja');
  expect(world.posts).toEqual([]);
});

test('admin content read errors never trigger generation or load a local draft', async ({ page }) => {
  const { world, screen } = await openAdminContent(page);
  await page.route(/\/api\/(?:admin\/)?pois\/2\/content\?/, route => route.fulfill({ status: 500, json: { title: 'Fixture unavailable' } }));
  await screen.getByRole('button', { name: /Bưu điện Trung tâm/ }).click();
  await expect(screen).toContainText('Không đọc được nội dung từ API');
  await expect(screen.getByText('Có lỗi', { exact: true })).toBeVisible();
  await expect(screen.getByLabel(/Nội dung đã lưu/)).toHaveValue('');
  await expect(screen.getByRole('button', { name: 'Chuẩn bị nội dung & audio' })).toBeDisabled();
  expect(world.posts).toEqual([]);
});

test('admin publishes and edits suggested routes; users get ordered updates and stop using hidden or deleted routes', async ({ page, browser }) => {
  const routes = [];
  let version = 0;
  const dto = (id, body) => ({ id, ...body, updatedAtUtc: new Date().toISOString(), unavailablePoiIds: [], version: String(++version),
    pois: body.poiIds.map(id => ({ ...pois.find(poi => poi.id === id), isPublic: true })) });
  await openAdminContent(page);
  await page.route('**/api/admin/routes**', async route => {
    const method = route.request().method(); const id = Number(new URL(route.request().url()).pathname.split('/').at(-1));
    if (method === 'GET') return route.fulfill({ json: routes });
    if (method === 'POST') { const created = dto(1, route.request().postDataJSON()); routes.push(created); return route.fulfill({ json: created }); }
    if (method === 'PUT') { const updated = dto(id, route.request().postDataJSON()); routes.splice(routes.findIndex(item => item.id === id), 1, updated); return route.fulfill({ json: updated }); }
    routes.splice(routes.findIndex(item => item.id === id), 1); return route.fulfill({ status: 204 });
  });
  await page.getByRole('button', { name: 'Lộ trình gợi ý', exact: true }).click();
  const admin = page.getByRole('region', { name: 'Quản lý lộ trình' });
  await admin.getByLabel('Tên lộ trình', { exact: true }).fill('Fixture route');
  await admin.getByLabel(/ID POI theo thứ tự/).fill('2,1');
  await admin.getByLabel('Trạng thái lộ trình').selectOption('Published');
  await admin.getByRole('button', { name: 'Tạo lộ trình', exact: true }).click();
  await expect(admin.getByRole('heading', { name: 'Fixture route' })).toBeVisible();
  const context = await browser.newContext({ baseURL: new URL(page.url()).origin });
  try {
    const user = await context.newPage(); const world = await setup(user);
    await user.route('**/api/routes', route => route.fulfill({ json: routes.filter(item => item.status === 'Published') }));
    await user.route(/\/api\/routes\/\d+\/preview$/, route => {
      const body = route.request().postDataJSON(); const selected = routes.find(item => item.status === 'Published');
      const stops = selected.poiIds.map((id, index) => ({ ...pois[id - 1], poiId: id, order: index + 1 }));
      return route.fulfill({ json: { start: { latitude: body.startLatitude, longitude: body.startLongitude }, stops,
        totalDistanceMeters: 1234, totalDurationSeconds: 777, algorithm: 'adminOrder', attribution: 'ORS fixture',
        geometry: { type: 'LineString', coordinates: [[body.startLongitude, body.startLatitude], ...stops.map(poi => [poi.longitude, poi.latitude])] } } });
    });
    await open(user); await user.locator('.map-locate').click();
    const suggestions = user.getByRole('region', { name: 'Lộ trình gợi ý' });
    await suggestions.getByRole('button', { name: 'Lộ trình gợi ý', exact: true }).click();
    await suggestions.getByRole('button', { name: /Fixture route/ }).click();
    const order = suggestions.getByRole('list', { name: 'Thứ tự POI của lộ trình' });
    await expect(order.getByRole('listitem')).toHaveText([pois[1].name, pois[0].name]);
    await expect(user.locator('.route-result')).toBeVisible();
    await admin.getByRole('button', { name: 'Sửa Fixture route', exact: true }).click();
    await admin.getByLabel('Tên lộ trình', { exact: true }).fill('Updated route');
    await admin.getByLabel(/ID POI theo thứ tự/).fill('1,2');
    await admin.getByRole('button', { name: 'Lưu lộ trình', exact: true }).click();
    await expect(admin.getByRole('heading', { name: 'Updated route' })).toBeVisible();
    await suggestions.getByRole('button', { name: 'Làm mới gợi ý' }).click();
    await expect(order.getByRole('listitem')).toHaveText([pois[0].name, pois[1].name]);
    await expect(user.locator('.route-result')).toHaveCount(0);
    await user.locator('.route-submit').click(); await expect(user.locator('.route-result')).toBeVisible();
    await admin.getByRole('button', { name: 'Sửa Updated route', exact: true }).click();
    await admin.getByLabel('Trạng thái lộ trình').selectOption('Hidden');
    await admin.getByRole('button', { name: 'Lưu lộ trình', exact: true }).click();
    await expect.poll(() => routes[0].status).toBe('Hidden');
    await suggestions.getByRole('button', { name: 'Làm mới gợi ý' }).click();
    await expect(user.locator('.route-error')).toContainText('Lộ trình đã bị ẩn/xóa');
    await expect(order).toHaveCount(0); await expect(user.locator('.route-result')).toHaveCount(0);
    await admin.getByRole('button', { name: 'Sửa Updated route', exact: true }).click();
    await admin.getByLabel('Trạng thái lộ trình').selectOption('Published');
    await admin.getByRole('button', { name: 'Lưu lộ trình', exact: true }).click();
    await expect.poll(() => routes[0].status).toBe('Published');
    await suggestions.getByRole('button', { name: 'Làm mới gợi ý' }).click();
    await suggestions.getByRole('button', { name: /Updated route/ }).click(); await expect(user.locator('.route-result')).toBeVisible();
    page.once('dialog', dialog => dialog.accept()); await admin.getByRole('button', { name: 'Xóa Updated route', exact: true }).click();
    await expect(admin).toContainText('Chưa có lộ trình.');
    await suggestions.getByRole('button', { name: 'Làm mới gợi ý' }).click();
    await expect(order).toHaveCount(0); await expect(user.locator('.route-result')).toHaveCount(0);
    expect(world.events.filter(event => event.eventType === 'route_selection').length).toBeGreaterThan(0);
    expect(world.posts).toEqual([]);
  } finally { await context.close(); }
});

test('playback events follow actual playing, deduplicate buffering and do not block audio on statistics failure', async ({ page }) => {
  const world = await setup(page); await open(page); await select(page);
  await page.evaluate(() => { window.__blockAudio = true; });
  await page.locator('.play-button').click(); await expect(page.getByRole('status').filter({ hasText: 'Trình duyệt chặn tự phát' })).toBeVisible();
  expect(world.events).toEqual([]);
  await page.evaluate(() => { window.__blockAudio = false; }); await page.locator('.play-button').click();
  await expect.poll(() => world.events.length).toBe(1);
  expect(world.events[0]).toMatchObject({ eventType: 'manual_play', poiId: 1, language: 'vi', audioId: 1 });
  await page.locator('audio').evaluate(audio => { audio.dispatchEvent(new Event('playing')); audio.dispatchEvent(new Event('playing')); });
  expect(world.events).toHaveLength(1);
  await page.route('**/api/usage-events', route => { world.events.push(route.request().postDataJSON()); return route.fulfill({ status: 500, json: { title: 'Stats fixture unavailable' } }); });
  await page.locator('.play-button').click(); await page.locator('.play-button').click();
  await expect.poll(() => world.events.length).toBe(2); await expect(page.locator('.play-button')).toHaveAttribute('aria-label', 'Tạm dừng');
  expect(world.events[0].eventId).not.toBe(world.events[1].eventId);
  expect(world.events[0].guestId).toBe(world.events[1].guestId);
});

test('suggested routes show read errors without falling back to sample routes', async ({ page }) => {
  await setup(page); await page.route('**/api/routes', route => route.fulfill({ status: 500, json: { title: 'Fixture unavailable' } }));
  await open(page); const suggestions = page.getByRole('region', { name: 'Lộ trình gợi ý' });
  await suggestions.getByRole('button', { name: 'Lộ trình gợi ý', exact: true }).click();
  await expect(suggestions).toContainText('Không tải được lộ trình gợi ý');
  await expect(suggestions).not.toContainText('Chưa có lộ trình gợi ý được xuất bản');
  await expect(suggestions.locator('.route-choice')).toHaveCount(0);
});

test('admin distinguishes failure history, active blocks, SQL logs and unavailable legacy details', async ({ page }) => {
  await setup(page);
  await page.addInitScript(() => sessionStorage.setItem('voicemap.admin.access-token', 'fixture-dashboard-token'));
  const now = new Date().toISOString();
  const profile = { id: 1, username: 'fixture', fullName: 'Admin fixture', status: 'Active' };
  let summary = {
    generatedAtUtc: now, health: { api: 'online', database: 'online', pipeline: 'online' },
    pipeline: { queued: 0, processing: 0, failed: 0, errorRatePercent: null, avgResponseMs: null,
      recentErrors: [], logsAvailable: true, isBlocked: false,
      reasons: [{ code: 'Ready', message: 'Sẵn sàng nhận yêu cầu mới.', retryAtUtc: null, requiresOperatorAction: false }],
      failedJobs: [], history: { startedAtUtc: now, retentionMinutes: 30, maxCompletedJobs: 256, lostOnRestart: true } },
    usage: { activeUsers: null, autoPlayCount: null, manualPlayCount: null, listensByPoi: [], listensByLanguage: [], topRoute: null },
  };
  const writes = [];
  await page.route('**/api/admin/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() !== 'GET') { writes.push(path); return route.abort(); }
    const data = path === '/api/admin/auth/me' ? profile : path === '/api/admin/pois' ? []
      : path === '/api/admin/dashboard/summary' ? summary : null;
    return data ? route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) }) : route.abort();
  });
  await page.goto('/admin');
  const dashboard = page.getByRole('region', { name: 'Dashboard giám sát' });
  const toggle = dashboard.getByRole('button', { name: /Tác vụ lỗi/ });
  const refresh = () => dashboard.getByRole('button', { name: 'Làm mới', exact: true }).click();
  await expect(dashboard).toContainText('Không có chặn tạo nội dung mới');
  await toggle.click();
  const details = page.getByRole('article', { name: 'Chi tiết tác vụ lỗi' });
  await expect(details).toContainText('Không có tác vụ lỗi trong lịch sử queue');
  await expect(dashboard).toContainText('Mất lịch sử khi API khởi động lại');
  const job = { jobId: '00000000-0000-0000-0000-000000000002', poiId: 2, poiName: 'Bưu điện fixture', language: 'zh-hans',
    status: 'Failed', failedAtUtc: now, stage: 'upload', errorCode: 'SupabaseObjectMismatch', message: 'File audio không khớp.',
    retryAtUtc: null, requiresOperatorAction: false, recoveredAtUtc: null };
  summary.pipeline.failed = 1;
  summary.pipeline.failedJobs = [job];
  summary.pipeline.reasons = [{ code: 'PastFailures', message: 'Lỗi cũ không chặn tác vụ mới.', retryAtUtc: null, requiresOperatorAction: false }];
  await refresh();
  await expect(details).toContainText('Bưu điện fixture');
  await expect(details).toContainText('zh-hans');
  await expect(details).toContainText(job.jobId);
  await expect(details).toContainText('Upload / kiểm tra Supabase');
  await expect(details).toContainText('SupabaseObjectMismatch');
  await expect(dashboard).toContainText('Lịch sử queue có phạm vi riêng');
  await expect(dashboard).toContainText('Không có chặn tạo nội dung mới');
  summary.health.pipeline = 'warning'; summary.pipeline.isBlocked = true;
  summary.pipeline.reasons = [{ code: 'DailyQuotaExceeded', message: 'Đã chạm quota ngày.', retryAtUtc: now, requiresOperatorAction: false }];
  await refresh();
  await expect(dashboard).toContainText('Đang chặn tạo nội dung mới');
  await expect(dashboard).toContainText('Đã chạm quota ngày.');
  await expect(dashboard).toContainText('Có thể kiểm tra lại sau');
  summary.pipeline.reasons = [{ code: 'GenerationNotConfigured', message: 'Cấu hình Gemini hoặc Supabase còn thiếu.', retryAtUtc: null, requiresOperatorAction: true }];
  await refresh();
  await expect(dashboard).toContainText('Cấu hình Gemini hoặc Supabase còn thiếu.');
  summary.pipeline.isBlocked = false; summary.health.pipeline = 'online';
  summary.pipeline.reasons = [{ code: 'PastFailures', message: 'Lỗi cũ không chặn tác vụ mới.', retryAtUtc: null, requiresOperatorAction: false }];
  job.recoveredAtUtc = now; job.stage = null; job.poiName = null;
  await refresh();
  await expect(details).toContainText('Đã có lượt xử lý thành công');
  await expect(details).toContainText('Chưa ghi nhận');
  await expect(details).toContainText('Chưa tải được tên POI');
  await expect(dashboard).toContainText('Không có chặn tạo nội dung mới');
  summary.pipeline.logsAvailable = false;
  await refresh();
  await expect(dashboard).toContainText('Chưa đọc được nhật ký SQL');
  delete summary.pipeline.failedJobs; delete summary.pipeline.history;
  delete summary.pipeline.reasons; delete summary.pipeline.isBlocked;
  await refresh();
  await expect(details).toContainText('API hiện tại chưa cung cấp chi tiết tác vụ');
  await expect(dashboard).toContainText('API chưa cung cấp lý do cụ thể');
  expect(writes).toEqual([]);
});

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
