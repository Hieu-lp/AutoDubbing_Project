import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { adminRequest, useAdminMockData, type AdminPoi, type AdminProfile } from './services/adminPoiService';
import type { SavedRoute } from './types';

export function AdminRoutesPage({ token, pois }: { token: string; pois: AdminPoi[] }) {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ['admin-routes'], queryFn: ({ signal }) => adminRequest<SavedRoute[]>('/api/admin/routes', token, { signal }), enabled: !useAdminMockData });
  const [editing, setEditing] = useState<number | null>(null);
  const [name, setName] = useState(''); const [ids, setIds] = useState(''); const [status, setStatus] = useState('Hidden');
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
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
export function AdminHistoryPage({ token, kind, pois }: { token: string; kind: 'usage' | 'logs'; pois: AdminPoi[] }) {
  const [poiId, setPoiId] = useState(''); const [language, setLanguage] = useState(''); const [filter, setFilter] = useState('');
  const params = new URLSearchParams(); if (poiId) params.set('poiId', poiId); if (language.trim()) params.set('language', language.trim()); if (filter) params.set(kind === 'usage' ? 'eventType' : 'level', filter);
  const path = `/api/admin/${kind === 'usage' ? 'usage-events' : 'logs'}?${params}`;
  const query = useQuery({ queryKey: ['admin-history', path], queryFn: ({ signal }) => adminRequest<(UsageRow & LogRow)[]>(path, token, { signal }), enabled: !useAdminMockData, refetchInterval: 10_000 });
  if (useAdminMockData) return <p role="alert">Nhật ký và sự kiện cần API thật; tắt chế độ mock.</p>;
  return <section className="admin-management" aria-label={kind === 'usage' ? 'Sự kiện sử dụng' : 'Nhật ký hệ thống'}>
    <p>SQL · 24 giờ gần nhất · tối đa 100 bản ghi mới nhất · tự cập nhật mỗi 10 giây. Chỉ xem lịch sử, không nhập/sửa sự kiện.</p>
    <div className="admin-form-grid admin-management-form"><label className="admin-field">Lọc POI<select value={poiId} onChange={event => setPoiId(event.target.value)}><option value="">Tất cả POI</option>{pois.map(poi => <option key={poi.id} value={poi.id}>{poi.name}</option>)}</select></label><label className="admin-field">Lọc ngôn ngữ<input value={language} onChange={event => setLanguage(event.target.value)} placeholder="vi, en, de…" /></label><label className="admin-field">{kind === 'usage' ? 'Loại sự kiện' : 'Mức nhật ký'}<select value={filter} onChange={event => setFilter(event.target.value)}><option value="">Tất cả</option>{(kind === 'usage' ? ['auto_play', 'manual_play', 'route_selection'] : ['error', 'info', 'warning']).map(item => <option key={item}>{item}</option>)}</select></label><button type="button" className="admin-button admin-button-secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>Làm mới lịch sử</button></div>
    {query.isError && <p role="alert">Không tải được lịch sử.{query.data && ' Đang giữ kết quả đọc gần nhất.'}</p>}
    {query.isPending ? <p role="status">Đang tải lịch sử…</p> : query.data?.length === 0 && !query.isError ? <p>Chưa có bản ghi trong phạm vi đang chọn.</p> : query.data?.map(row => <article className="admin-management-card" key={row.id}><strong>{new Date(row.occurredAtUtc).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })}</strong><p>{kind === 'usage' ? `${row.eventType} · ${row.poiName ?? row.routeName ?? 'Không còn dữ liệu tên'} · ${row.language}` : `${row.level} · ${row.message}`}</p>{kind === 'logs' && <p>POI: {row.poiId ?? 'Chưa ghi nhận'} · Ngôn ngữ: {row.language ?? 'Chưa ghi nhận'} · Job: {row.jobId ?? 'Chưa ghi nhận'} · Bước: {row.stage ?? 'Chưa ghi nhận'} · Mã: {row.errorCode}</p>}</article>)}
  </section>;
}
