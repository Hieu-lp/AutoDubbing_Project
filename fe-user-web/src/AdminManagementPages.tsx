import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { adminRequest, useAdminMockData, type AdminPoi, type AdminProfile } from './services/adminPoiService';
import type { SavedRoute } from './types';
import { calculateRouteDistances, formatRouteDistance } from './services/routeService';

export function AdminRoutesPage({ token, pois }: { token: string; pois: AdminPoi[] }) {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ['admin-routes'], queryFn: ({ signal }) => adminRequest<SavedRoute[]>('/api/admin/routes', token, { signal }), enabled: !useAdminMockData });
  const [editing, setEditing] = useState<number | null>(null);
  const [name, setName] = useState(''); const [ids, setIds] = useState(''); const [status, setStatus] = useState('Hidden');
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const routeDistances = ids.trim()
    ? calculateRouteDistances(ids.split(',').map(value => Number(value.trim())), pois)
    : null;
  function clear() { setEditing(null); setName(''); setIds(''); setStatus('Hidden'); setError(''); }
  async function save(event: FormEvent) {
    event.preventDefault(); setError('');
    const poiIds = ids.split(',').map(value => Number(value.trim()));
    if (!poiIds.length || poiIds.some(id => !Number.isSafeInteger(id) || id <= 0) || new Set(poiIds).size !== poiIds.length) { setError('Nhập ID POI dương, phân cách bằng dấu phẩy, không trùng.'); return; }
    setBusy(true);
    try {
      await adminRequest(`/api/admin/routes${editing === null ? '' : `/${editing}`}`, token, { method: editing === null ? 'POST' : 'PUT', body: JSON.stringify({ name, poiIds, status }) });
      await client.invalidateQueries({ queryKey: ['admin-routes'] }); await client.invalidateQueries({ queryKey: ['suggested-routes'] }); clear();
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Không lưu được lộ trình.'); }
    finally { setBusy(false); }
  }
  async function remove(route: SavedRoute) {
    if (!window.confirm(`Xóa lộ trình “${route.name}”? Lịch sử sử dụng được giữ lại.`)) return;
    setBusy(true); setError('');
    try { await adminRequest(`/api/admin/routes/${route.id}`, token, { method: 'DELETE' }); await query.refetch(); await client.invalidateQueries({ queryKey: ['suggested-routes'] }); if (editing === route.id) clear(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Không xóa được lộ trình.'); }
    finally { setBusy(false); }
  }
  if (useAdminMockData) return <p role="alert">Quản lý lộ trình cần API thật; tắt chế độ mock.</p>;
  return <section className="admin-management" aria-label="Quản lý lộ trình">
    <p>Lộ trình gợi ý giữ đúng thứ tự POI. Chỉ lộ trình đã xuất bản, có toàn bộ POI công khai mới hiện bên khách.</p>
    {(query.isError || error) && <p role="alert">{error || 'Không tải được lộ trình.'} {query.data && 'Đang giữ dữ liệu đọc gần nhất.'}</p>}
    <form className="admin-form-grid admin-management-form" onSubmit={event => void save(event)}>
      <label className="admin-field">Tên lộ trình<input value={name} onChange={event => setName(event.target.value)} required maxLength={200} /></label>
      <label className="admin-field">Trạng thái lộ trình<select value={status} onChange={event => setStatus(event.target.value)}><option value="Hidden">Ẩn</option><option value="Published">Xuất bản</option></select></label>
      <label className="admin-field admin-field-wide">ID POI theo thứ tự, cách nhau bằng dấu phẩy<input value={ids} onChange={event => setIds(event.target.value)} required placeholder="Ví dụ: 2,1,3" /></label>
      {routeDistances && <div className="admin-field-wide">
        {routeDistances.legs.map(leg => <p key={`${leg.from.id}-${leg.to.id}`}>
          {leg.from.name} → {leg.to.name}: {formatRouteDistance(leg.distanceMeters)}
        </p>)}
        <strong>Tổng khoảng cách giữa các POI: {formatRouteDistance(routeDistances.totalDistanceMeters)}</strong>
        <small> Khoảng cách đường chim bay.</small>
      </div>}
      <p className="admin-field-wide">{pois.map(poi => `${poi.id}: ${poi.name}${poi.status === 'Hidden' ? ' (ẩn)' : ''}`).join(' · ')}</p>
      <div className="admin-management-actions"><button className="admin-button admin-button-primary" disabled={busy}>{editing === null ? 'Tạo lộ trình' : 'Lưu lộ trình'}</button><button type="button" className="admin-button admin-button-secondary" onClick={clear}>Nhập mới</button></div>
    </form>
    <button type="button" className="admin-button admin-button-secondary" onClick={() => void query.refetch()} disabled={query.isFetching}>Làm mới lộ trình</button>
    {query.isPending ? <p role="status">Đang tải lộ trình…</p> : query.data?.length === 0 && !query.isError ? <p>Chưa có lộ trình.</p> : query.data?.map(route => <article className="admin-management-card" key={route.id}>
      <h2>{route.name}</h2><p>{route.status === 'Published' ? 'Đã xuất bản' : 'Đang ẩn'} · {route.poiIds.map(id => `${id}: ${route.pois.find(poi => poi.id === id)?.name ?? 'Không còn tồn tại'}`).join(' → ')}</p>
      {route.unavailablePoiIds.length > 0 && <p role="alert">POI chưa công khai/không khả dụng: {route.unavailablePoiIds.join(', ')}. Khách chưa được xem lộ trình này.</p>}
      <div className="admin-management-actions"><button type="button" className="admin-button admin-button-secondary" onClick={() => { setEditing(route.id); setName(route.name); setIds(route.poiIds.join(',')); setStatus(route.status); setError(''); }}>Sửa {route.name}</button><button type="button" className="admin-button admin-button-secondary" disabled={busy} onClick={() => void remove(route)}>Xóa {route.name}</button></div>
    </article>)}
  </section>;
}

export function AdminAccountsPage({ token, current, onProfile }: { token: string; current: AdminProfile; onProfile: (profile: AdminProfile) => void }) {
  const query = useQuery({ queryKey: ['admin-users'], queryFn: ({ signal }) => adminRequest<AdminProfile[]>('/api/admin/users', token, { signal }), enabled: !useAdminMockData });
  const [editing, setEditing] = useState<number | null>(null); const [username, setUsername] = useState(''); const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState(''); const [status, setStatus] = useState('Active'); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  function edit(user?: AdminProfile) { setEditing(user?.id ?? null); setUsername(user?.username ?? ''); setFullName(user?.fullName ?? ''); setStatus(user?.status ?? 'Active'); setPassword(''); setError(''); }
  async function save(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      const saved = await adminRequest<AdminProfile>(`/api/admin/users${editing === null ? '' : `/${editing}`}`, token, { method: editing === null ? 'POST' : 'PUT', body: JSON.stringify({ username, fullName, status, password: password || undefined }) });
      if (saved.id === current.id) onProfile(saved);
      setPassword(''); await query.refetch(); edit();
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Không lưu được tài khoản.'); }
    finally { setBusy(false); }
  }
  if (useAdminMockData) return <p role="alert">Quản lý tài khoản cần API thật; tắt chế độ mock.</p>;
  return <section className="admin-management" aria-label="Quản lý tài khoản">
    <p>Mọi tài khoản trong hệ thống hiện có đều là quản trị viên. Không thể khóa admin hoạt động cuối cùng. Khách dùng web không cần đăng nhập.</p>
    {(error || query.isError) && <p role="alert">{error || 'Không tải được tài khoản.'}</p>}
    <form className="admin-form-grid admin-management-form" onSubmit={event => void save(event)}>
      <label className="admin-field">Tên đăng nhập<input value={username} onChange={event => setUsername(event.target.value)} required maxLength={100} autoComplete="off" /></label>
      <label className="admin-field">Họ tên<input value={fullName} onChange={event => setFullName(event.target.value)} required maxLength={200} /></label>
      <label className="admin-field">{editing === null ? 'Mật khẩu mới' : 'Đổi mật khẩu (để trống để giữ nguyên)'}<input type="password" value={password} onChange={event => setPassword(event.target.value)} minLength={8} maxLength={1024} required={editing === null} autoComplete="new-password" /></label>
      <label className="admin-field">Trạng thái tài khoản<select value={status} onChange={event => setStatus(event.target.value)}><option value="Active">Hoạt động</option><option value="Locked">Khóa</option></select></label>
      <div className="admin-management-actions"><button className="admin-button admin-button-primary" disabled={busy}>{editing === null ? 'Tạo tài khoản' : 'Lưu tài khoản'}</button><button type="button" className="admin-button admin-button-secondary" onClick={() => edit()}>Nhập mới</button></div>
    </form>
    {query.isPending ? <p role="status">Đang tải tài khoản…</p> : query.data?.map(user => <article className="admin-management-card" key={user.id}><h2>{user.fullName} {user.id === current.id && '(Bạn)'}</h2><p>{user.username} · {user.status === 'Active' ? 'Hoạt động' : 'Đã khóa'}</p><button type="button" className="admin-button admin-button-secondary" onClick={() => edit(user)}>Sửa tài khoản {user.username}</button></article>)}
  </section>;
}

type UsageRow = { id: number; poiId: number | null; poiName: string | null; language: string; eventType: string; routeName: string | null; occurredAtUtc: string };
type LogRow = { id: number; poiId: number | null; language: string | null; jobId: string | null; stage: string | null; errorCode: string; message: string; level: string; occurredAtUtc: string };
type UsagePoiStatistics = { poiId: number; poiName: string; listens: number; listeners: number; peakHour: number | null; peakListens: number };
type UsageHourStatistics = { hour: number; listens: number; topPoiId: number | null; topPoiName: string | null; topPoiListens: number };
type UsageStatistics = {
  sinceUtc: string; untilUtc: string; timeZone: string; totalListens: number; uniqueListeners: number;
  topPoi: UsagePoiStatistics | null; peakHour: UsageHourStatistics | null;
  byPoi: UsagePoiStatistics[]; byHour: UsageHourStatistics[];
};

function usageHourLabel(hour: number) {
  return `${String(hour).padStart(2, '0')}:00–${String(hour + 1).padStart(2, '0')}:00`;
}

export function AdminHistoryPage({ token, kind, pois }: { token: string; kind: 'usage' | 'logs'; pois: AdminPoi[] }) {
  const [poiId, setPoiId] = useState(''); const [language, setLanguage] = useState(''); const [filter, setFilter] = useState('');
  const [since, setSince] = useState(''); const [until, setUntil] = useState('');
  const [startHour, setStartHour] = useState('0'); const [endHour, setEndHour] = useState('24');
  const sinceDate = since ? new Date(`${since}+07:00`) : null;
  const untilDate = until ? new Date(`${until}+07:00`) : null;
  const effectiveUntil = untilDate?.getTime() ?? Date.now();
  const effectiveSince = sinceDate?.getTime() ?? effectiveUntil - 86_400_000;
  const rangeError = kind !== 'usage' ? ''
    : !Number.isFinite(effectiveSince) || !Number.isFinite(effectiveUntil) ? 'Thời gian không hợp lệ.'
    : effectiveSince >= effectiveUntil ? 'Thời gian bắt đầu phải trước thời gian kết thúc.'
    : effectiveSince < Date.now() - 31 * 86_400_000 || effectiveUntil - effectiveSince > 31 * 86_400_000 ? 'Chỉ xem dữ liệu trong 31 ngày gần nhất.'
    : startHour === endHour ? 'Giờ bắt đầu và kết thúc phải khác nhau; chọn 00–24 để xem cả ngày.' : '';
  const params = new URLSearchParams(); if (poiId) params.set('poiId', poiId); if (language.trim()) params.set('language', language.trim()); if (filter) params.set(kind === 'usage' ? 'eventType' : 'level', filter);
  if (kind === 'usage') {
    if (sinceDate && Number.isFinite(sinceDate.getTime())) params.set('sinceUtc', sinceDate.toISOString());
    if (untilDate && Number.isFinite(untilDate.getTime())) params.set('untilUtc', untilDate.toISOString());
    params.set('startHour', startHour); params.set('endHour', endHour);
  }
  const path = `/api/admin/${kind === 'usage' ? 'usage-events' : 'logs'}?${params}`;
  const query = useQuery({ queryKey: ['admin-history', path], queryFn: ({ signal }) => adminRequest<(UsageRow & LogRow)[]>(path, token, { signal }), enabled: !useAdminMockData && !rangeError, refetchInterval: 10_000 });
  const statisticsPath = `/api/admin/usage-events/statistics?${params}`;
  const statistics = useQuery({ queryKey: ['admin-usage-statistics', statisticsPath],
    queryFn: ({ signal }) => adminRequest<UsageStatistics>(statisticsPath, token, { signal }),
    enabled: !useAdminMockData && kind === 'usage' && !rangeError, refetchInterval: 10_000 });
  if (useAdminMockData) return <p role="alert">Nhật ký và sự kiện cần API thật; tắt chế độ mock.</p>;
  return <section className="admin-management" aria-label={kind === 'usage' ? 'Sự kiện sử dụng' : 'Nhật ký hệ thống'}>
    <p>{kind === 'usage' ? 'SQL · chọn khoảng ngày và khung giờ theo giờ Việt Nam (UTC+7) · mặc định 24 giờ gần nhất.' : 'SQL · 24 giờ gần nhất.'} Lịch sử tối đa 100 bản ghi mới nhất · tự cập nhật mỗi 10 giây.</p>
    <div className="admin-form-grid admin-management-form">
      <label className="admin-field">Lọc POI<select value={poiId} onChange={event => setPoiId(event.target.value)}><option value="">Tất cả POI</option>{pois.map(poi => <option key={poi.id} value={poi.id}>{poi.name}</option>)}</select></label>
      <label className="admin-field">Lọc ngôn ngữ<input value={language} onChange={event => setLanguage(event.target.value)} placeholder="vi, en, de…" /></label>
      <label className="admin-field">{kind === 'usage' ? 'Loại sự kiện' : 'Mức nhật ký'}<select value={filter} onChange={event => setFilter(event.target.value)}><option value="">Tất cả</option>{(kind === 'usage' ? ['auto_play', 'manual_play', 'route_selection'] : ['error', 'info', 'warning']).map(item => <option key={item}>{item}</option>)}</select></label>
      {kind === 'usage' && <>
        <label className="admin-field">Từ ngày, giờ (Việt Nam)<input type="datetime-local" value={since} onChange={event => setSince(event.target.value)} /></label>
        <label className="admin-field">Đến trước ngày, giờ (Việt Nam)<input type="datetime-local" value={until} onChange={event => setUntil(event.target.value)} /></label>
        <label className="admin-field">Khung giờ mỗi ngày: từ<select value={startHour} onChange={event => setStartHour(event.target.value)}>{Array.from({ length: 24 }, (_, hour) => <option key={hour} value={hour}>{String(hour).padStart(2, '0')}:00</option>)}</select></label>
        <label className="admin-field">Đến trước<select value={endHour} onChange={event => setEndHour(event.target.value)}>{Array.from({ length: 25 }, (_, hour) => <option key={hour} value={hour}>{String(hour).padStart(2, '0')}:00</option>)}</select></label>
        <p className="admin-field-wide">Để trống ngày kết thúc để cập nhật đến hiện tại; để trống ngày bắt đầu để lấy 24 giờ trước mốc kết thúc. Khung giờ có thể qua đêm, ví dụ 22:00–06:00.</p>
        <button type="button" className="admin-button admin-button-secondary" onClick={() => { setSince(''); setUntil(''); setStartHour('0'); setEndHour('24'); }}>24 giờ gần nhất · cả ngày</button>
      </>}
      <button type="button" className="admin-button admin-button-secondary" disabled={!!rangeError || query.isFetching || (kind === 'usage' && statistics.isFetching)} onClick={() => { void query.refetch(); if (kind === 'usage') void statistics.refetch(); }}>Làm mới lịch sử{kind === 'usage' && ' và thống kê'}</button>
    </div>
    {rangeError && <p role="alert">{rangeError}</p>}
    {kind === 'usage' && !rangeError && <section className="admin-usage-statistics" aria-label="Thống kê lượt nghe theo POI và khung giờ">
      <h2>Thống kê lượt nghe</h2>
      <p>Tính toàn bộ lượt bắt đầu nghe tự động/thủ công trong bộ lọc, không giới hạn 100 bản ghi. Người nghe tính theo mã khách ẩn danh, không phải số người đang nghe trực tiếp.</p>
      {statistics.isPending && <p role="status">Đang tải thống kê…</p>}
      {statistics.isError && <p role="alert">Không tải được thống kê.{statistics.data && ' Đang hiển thị kết quả gần nhất.'}</p>}
      {statistics.data && <>
        <p>{new Date(statistics.data.sinceUtc).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })} → {new Date(statistics.data.untilUtc).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })} · Giờ Việt Nam</p>
        <div className="admin-usage-summary">
          <article className="admin-management-card"><h3>Tổng lượt nghe</h3><strong>{statistics.data.totalListens.toLocaleString('vi-VN')}</strong></article>
          <article className="admin-management-card"><h3>Người nghe khác nhau</h3><strong>{statistics.data.uniqueListeners.toLocaleString('vi-VN')}</strong></article>
          <article className="admin-management-card"><h3>POI được nghe nhiều nhất</h3><strong>{statistics.data.topPoi?.poiName ?? 'Chưa có lượt nghe'}</strong>{statistics.data.topPoi && <p>{statistics.data.topPoi.listens} lượt · {statistics.data.topPoi.listeners} người nghe{statistics.data.topPoi.peakHour !== null && <> · cao điểm {usageHourLabel(statistics.data.topPoi.peakHour)} ({statistics.data.topPoi.peakListens} lượt)</>}</p>}</article>
          <article className="admin-management-card"><h3>Khung giờ nghe nhiều nhất</h3><strong>{statistics.data.peakHour ? usageHourLabel(statistics.data.peakHour.hour) : 'Chưa có lượt nghe'}</strong>{statistics.data.peakHour && <p>{statistics.data.peakHour.listens} lượt · POI dẫn đầu: {statistics.data.peakHour.topPoiName} ({statistics.data.peakHour.topPoiListens} lượt)</p>}</article>
        </div>
        {statistics.data.byPoi.length === 0 ? <p>Không có lượt nghe trong phạm vi đang chọn.</p> : <>
          <article className="admin-management-card"><h3>Xếp hạng POI và giờ cao điểm</h3><div className="admin-table-scroll"><table className="admin-table admin-usage-table">
            <thead><tr><th>POI</th><th>Lượt nghe</th><th>Người nghe</th><th>Giờ cao điểm</th><th>Lượt trong giờ cao điểm</th></tr></thead>
            <tbody>{statistics.data.byPoi.map(poi => <tr key={poi.poiId}><td>{poi.poiName} (#{poi.poiId})</td><td>{poi.listens}</td><td>{poi.listeners}</td><td>{poi.peakHour === null ? '—' : usageHourLabel(poi.peakHour)}</td><td>{poi.peakListens}</td></tr>)}</tbody>
          </table></div></article>
          <article className="admin-management-card"><h3>POI được nghe nhiều nhất trong từng khung giờ</h3><p>Cộng gộp cùng giờ qua các ngày trong khoảng đã chọn. Các kết quả bằng nhau ưu tiên giờ sớm hơn và ID POI nhỏ hơn.</p><div className="admin-table-scroll"><table className="admin-table admin-usage-table">
            <thead><tr><th>Khung giờ (UTC+7)</th><th>Tổng lượt nghe</th><th>POI dẫn đầu</th><th>Lượt nghe POI dẫn đầu</th></tr></thead>
            <tbody>{statistics.data.byHour.filter(hour => hour.listens > 0).map(hour => <tr key={hour.hour}><td>{usageHourLabel(hour.hour)}</td><td>{hour.listens}</td><td>{hour.topPoiName} (#{hour.topPoiId})</td><td>{hour.topPoiListens}</td></tr>)}</tbody>
          </table></div></article>
        </>}
      </>}
    </section>}
    {query.isError && <p role="alert">Không tải được lịch sử.{query.data && ' Đang giữ kết quả đọc gần nhất.'}</p>}
    {!rangeError && <>
      {kind === 'usage' && <h2>Lịch sử sự kiện · tối đa 100 bản ghi mới nhất</h2>}
      {query.isPending ? <p role="status">Đang tải lịch sử…</p> : query.data?.length === 0 && !query.isError ? <p>Chưa có bản ghi trong phạm vi đang chọn.</p> : query.data?.map(row => <article className="admin-management-card" key={row.id}><strong>{new Date(row.occurredAtUtc).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })}</strong><p>{kind === 'usage' ? `${row.eventType} · ${row.poiName ?? row.routeName ?? 'Không còn dữ liệu tên'} · ${row.language}` : `${row.level} · ${row.message}`}</p>{kind === 'logs' && <p>POI: {row.poiId ?? 'Chưa ghi nhận'} · Ngôn ngữ: {row.language ?? 'Chưa ghi nhận'} · Job: {row.jobId ?? 'Chưa ghi nhận'} · Bước: {row.stage ?? 'Chưa ghi nhận'} · Mã: {row.errorCode}</p>}</article>)}
    </>}
  </section>;
}
