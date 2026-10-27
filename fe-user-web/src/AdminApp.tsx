import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import {
  AdminApiError,
  clearAdminToken,
  createAdminPoi,
  deleteAdminPoi,
  getAdminPois,
  getAdminProfile,
  getAdminToken,
  loginAdmin,
  saveAdminToken,
  updateAdminPoi,
  useAdminMockData,
  type AdminPoi,
  type AdminPoiInput,
  type AdminProfile,
} from './services/adminPoiService';
import AdminDashboard from './AdminDashboard';
import { TranslationAudioPage } from './AdminWorkflowPages';
import { AdminAccountsPage, AdminHistoryPage, AdminRoutesPage } from './AdminManagementPages';
import { useQueryClient } from '@tanstack/react-query';

type Notice = { kind: 'success' | 'error'; text: string };
type Filter = 'all' | 'published' | 'hidden';
type AdminSection = 'overview' | 'places' | 'pipeline' | 'routes' | 'accounts' | 'usage' | 'logs';

const emptyForm: AdminPoiInput = {
  name: '',
  latitude: 10.7769,
  longitude: 106.7009,
  proximityRadius: 50,
  thumbnailUrl: null,
  originalDescription: '',
  originalLanguage: 'vi',
  status: 'Published',
};

function Icon({ name, size = 18 }: { name: string; size?: number }) {
  const props = {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.8,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true as const,
  };

  switch (name) {
    case 'pin': return <svg {...props}><path d="M20 10c0 5-8 12-8 12S4 15 4 10a8 8 0 1 1 16 0Z" /><circle cx="12" cy="10" r="2.5" /></svg>;
    case 'grid': return <svg {...props}><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /></svg>;
    case 'plus': return <svg {...props}><path d="M12 5v14M5 12h14" /></svg>;
    case 'search': return <svg {...props}><circle cx="10.8" cy="10.8" r="6.8" /><path d="m16 16 4.5 4.5" /></svg>;
    case 'edit': return <svg {...props}><path d="m14 5 5 5M4 20l4.6-.9L19 8.7a2.1 2.1 0 0 0-3-3L5.6 16.1 4 20Z" /></svg>;
    case 'trash': return <svg {...props}><path d="M4 7h16M10 11v6M14 11v6M5.5 7l1 13h11l1-13M9 7V4h6v3" /></svg>;
    case 'close': return <svg {...props}><path d="m18 6-12 12M6 6l12 12" /></svg>;
    case 'logout': return <svg {...props}><path d="M10 17l5-5-5-5M15 12H3M12 3h6a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-6" /></svg>;
    case 'refresh': return <svg {...props}><path d="M20 7v5h-5M4 17v-5h5" /><path d="M5.6 9a7 7 0 0 1 11.6-2L20 12M4 12l2.8 5a7 7 0 0 0 11.6-2" /></svg>;
    case 'globe': return <svg {...props}><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" /></svg>;
    case 'arrow': return <svg {...props}><path d="M7 17 17 7M7 7h10v10" /></svg>;
    case 'check': return <svg {...props}><path d="m5 12 4 4L19 6" /></svg>;
    case 'menu': return <svg {...props}><path d="M4 7h16M4 12h16M4 17h16" /></svg>;
    case 'shield': return <svg {...props}><path d="M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11Z" /><path d="m9 12 2 2 4-4" /></svg>;
    case 'activity': return <svg {...props}><path d="M3 12h4l3-8 4 16 3-8h4" /></svg>;
    default: return <svg {...props}><circle cx="12" cy="12" r="9" /><path d="M12 8v4m0 4h.01" /></svg>;
  }
}

function LoginScreen({ onLogin }: { onLogin: (username: string, password: string) => Promise<void> }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setLoading(true);
    try {
      await onLogin(username, password);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Đăng nhập thất bại.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="admin-login-page">
      <section className="admin-login-card">
        <a className="admin-brand" href="/" aria-label="VoiceMap">
          <span className="admin-brand-mark"><Icon name="pin" size={20} /></span>
          <span>voice<span>map</span></span>
        </a>
        <div className="admin-login-emblem"><Icon name="shield" size={24} /></div>
        <p className="admin-overline">KHÔNG GIAN QUẢN TRỊ</p>
        <h1>Chào mừng trở lại</h1>
        <p className="admin-login-subtitle">Đăng nhập để quản lý các địa điểm của bạn.</p>
        <form className="admin-login-form" onSubmit={submit}>
          <label>Tên đăng nhập<input autoComplete="username" autoFocus required value={username} onChange={(event) => setUsername(event.target.value)} placeholder="Nhập tên đăng nhập" /></label>
          <label>Mật khẩu<input autoComplete="current-password" required type="password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Nhập mật khẩu" /></label>
          {error && <div className="admin-alert admin-alert-error" role="alert">{error}</div>}
          <button className="admin-button admin-button-primary admin-login-submit" type="submit" disabled={loading}>
            {loading ? 'Đang xác thực…' : 'Đăng nhập quản trị'} <span aria-hidden="true">→</span>
          </button>
        </form>
        <a className="admin-back-link" href="/">← Quay lại trang khám phá</a>
      </section>
      <p className="admin-login-footer">VoiceMap Admin <span>•</span> Quản lý nội dung địa điểm</p>
    </main>
  );
}

function PoiModal({ poi, saving, onClose, onSave }: {
  poi: AdminPoi | null;
  saving: boolean;
  onClose: () => void;
  onSave: (input: AdminPoiInput) => Promise<void>;
}) {
  const [form, setForm] = useState<AdminPoiInput>(poi ? {
    name: poi.name,
    latitude: poi.latitude,
    longitude: poi.longitude,
    proximityRadius: poi.proximityRadius,
    thumbnailUrl: poi.thumbnailUrl,
    originalDescription: poi.originalDescription,
    originalLanguage: poi.originalLanguage,
    status: poi.status,
  } : emptyForm);
  const [error, setError] = useState('');

  function update<K extends keyof AdminPoiInput>(key: K, value: AdminPoiInput[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    try {
      await onSave({ ...form, thumbnailUrl: form.thumbnailUrl?.trim() || null });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Không thể lưu địa điểm.');
    }
  }

  return (
    <div className="admin-modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !saving) onClose(); }}>
      <section className="admin-modal" role="dialog" aria-modal="true" aria-labelledby="poi-modal-title">
        <header className="admin-modal-header">
          <div><p className="admin-overline">{poi ? 'CẬP NHẬT NỘI DUNG' : 'THÊM VÀO BẢN ĐỒ'}</p><h2 id="poi-modal-title">{poi ? 'Chỉnh sửa địa điểm' : 'Thêm địa điểm mới'}</h2></div>
          <button className="admin-icon-button" type="button" aria-label="Đóng" onClick={onClose} disabled={saving}><Icon name="close" /></button>
        </header>
        <form onSubmit={submit}>
          <div className="admin-form-grid">
            <label className="admin-field admin-field-wide">Tên địa điểm<input autoFocus maxLength={200} required value={form.name} onChange={(event) => update('name', event.target.value)} placeholder="Ví dụ: Nhà thờ Đức Bà" /></label>
            <label className="admin-field">Vĩ độ<input required type="number" min={-90} max={90} step="any" value={form.latitude} onChange={(event) => update('latitude', Number(event.target.value))} /></label>
            <label className="admin-field">Kinh độ<input required type="number" min={-180} max={180} step="any" value={form.longitude} onChange={(event) => update('longitude', Number(event.target.value))} /></label>
            <label className="admin-field">Bán kính kích hoạt (m)<input required type="number" min={1} max={10000} value={form.proximityRadius} onChange={(event) => update('proximityRadius', Number(event.target.value))} /></label>
            <label className="admin-field">Ngôn ngữ gốc<select value={form.originalLanguage} onChange={(event) => update('originalLanguage', event.target.value)}><option value="vi">Tiếng Việt</option><option value="en">English</option><option value="zh-hans">中文（简体）</option><option value="ja">日本語</option><option value="ko">한국어</option><option value="fr">Français</option><option value="es">Español</option></select></label>
            <label className="admin-field admin-field-wide">Ảnh đại diện <span className="admin-field-optional">(không bắt buộc)</span><input type="url" maxLength={2048} value={form.thumbnailUrl ?? ''} onChange={(event) => update('thumbnailUrl', event.target.value)} placeholder="https://example.com/anh-dia-diem.jpg" /></label>
            <label className="admin-field admin-field-wide">Mô tả gốc<textarea required maxLength={10000} rows={4} value={form.originalDescription} onChange={(event) => update('originalDescription', event.target.value)} placeholder="Giới thiệu ngắn về địa điểm này…" /></label>
            <label className="admin-field admin-field-wide">Trạng thái<select value={form.status} onChange={(event) => update('status', event.target.value as AdminPoiInput['status'])}><option value="Published">Đã xuất bản</option><option value="Hidden">Ẩn</option></select><span className="admin-field-help">Địa điểm ẩn sẽ không xuất hiện trên bản đồ dành cho khách.</span></label>
          </div>
          {error && <div className="admin-alert admin-alert-error" role="alert">{error}</div>}
          <footer className="admin-modal-footer"><button className="admin-button admin-button-secondary" type="button" onClick={onClose} disabled={saving}>Hủy</button><button className="admin-button admin-button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : poi ? 'Lưu thay đổi' : 'Tạo địa điểm'}</button></footer>
        </form>
      </section>
    </div>
  );
}

export default function AdminApp() {
  const queryClient = useQueryClient();
  const [token, setToken] = useState<string | null>(null);
  const [admin, setAdmin] = useState<AdminProfile | null>(null);
  const [pois, setPois] = useState<AdminPoi[]>([]);
  const [poisLoaded, setPoisLoaded] = useState(false);
  const [poisError, setPoisError] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [modalPoi, setModalPoi] = useState<AdminPoi | null | undefined>(undefined);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [activeSection, setActiveSection] = useState<AdminSection>('overview');

  const logout = useCallback(() => {
    clearAdminToken();
    queryClient.clear();
    setToken(null);
    setAdmin(null);
    setPois([]);
    setPoisLoaded(false); setPoisError('');
  }, [queryClient]);

  const loadPois = useCallback(async (accessToken: string) => {
    try {
      setPois(await getAdminPois(accessToken));
      setPoisLoaded(true); setPoisError('');
    } catch (error) {
      if (error instanceof AdminApiError && error.status === 401) {
        logout();
        setNotice({ kind: 'error', text: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.' });
        return;
      }
      const message = error instanceof Error ? error.message : 'Không tải được danh sách địa điểm.';
      setPoisError(message); setNotice({ kind: 'error', text: message });
    } finally {
      setLoading(false);
    }
  }, [logout]);

  useEffect(() => {
    document.title = 'VoiceMap — Quản trị địa điểm';
    const savedToken = getAdminToken();
    if (!savedToken) {
      setLoading(false);
      return;
    }
    let active = true;
    void getAdminProfile(savedToken).then((profile) => {
      if (!active) return;
      setToken(savedToken);
      setAdmin(profile);
      return loadPois(savedToken);
    }).catch((error: unknown) => {
      if (!active) return;
      clearAdminToken();
      setLoading(false);
      if (!(error instanceof AdminApiError && error.status === 401)) {
        setNotice({ kind: 'error', text: error instanceof Error ? error.message : 'Không kết nối được API.' });
      }
    });
    return () => { active = false; };
  }, [loadPois]);

  async function handleLogin(username: string, password: string) {
    const result = await loginAdmin(username, password);
    saveAdminToken(result.accessToken);
    setToken(result.accessToken);
    setAdmin(result.admin);
    setLoading(true);
    setNotice(null);
    await loadPois(result.accessToken);
  }

  const visiblePois = useMemo(() => pois.filter((poi) => {
    const matchesQuery = `${poi.name} ${poi.originalDescription}`.toLocaleLowerCase('vi').includes(query.trim().toLocaleLowerCase('vi'));
    const matchesStatus = filter === 'all' || (filter === 'published' ? poi.status === 'Published' : poi.status === 'Hidden');
    return matchesQuery && matchesStatus;
  }), [filter, pois, query]);
  const publishedCount = pois.filter((poi) => poi.status === 'Published').length;
  const hiddenCount = pois.length - publishedCount;
  const publishedPercent = pois.length ? Math.round((publishedCount / pois.length) * 100) : 0;
  const sectionDetails: Record<AdminSection, { title: string; description: string }> = {
    overview: { title: 'Dashboard giám sát', description: 'Theo dõi tình trạng hệ thống và mức sử dụng theo thời gian thực.' },
    places: { title: 'Địa điểm POI', description: 'Quản lý POI, tọa độ, phạm vi lân cận, ảnh thumbnail và mô tả gốc.' },
    pipeline: { title: 'Bản dịch & audio', description: 'Theo dõi trạng thái pipeline và nội dung đa ngôn ngữ của từng POI.' },
    routes: { title: 'Lộ trình gợi ý', description: 'Quản lý lộ trình độc lập và thứ tự POI được xuất bản cho khách.' },
    accounts: { title: 'Tài khoản quản trị', description: 'Thông tin cá nhân, tài khoản và trạng thái truy cập.' },
    usage: { title: 'Sự kiện sử dụng', description: 'Lượt nghe và sử dụng lộ trình đã ghi vào SQL.' },
    logs: { title: 'Nhật ký hệ thống', description: 'Lịch sử SQL đã lọc an toàn; phân biệt với queue trong RAM.' },
  };

  async function savePoi(input: AdminPoiInput) {
    if (!token) return;
    setSaving(true);
    try {
      if (modalPoi) {
        await updateAdminPoi(token, modalPoi.id, input);
        setNotice({ kind: 'success', text: 'Đã cập nhật địa điểm.' });
      } else {
        await createAdminPoi(token, input);
        setNotice({ kind: 'success', text: 'Đã thêm địa điểm mới.' });
      }
      setModalPoi(undefined);
      await loadPois(token);
    } catch (error) {
      if (error instanceof AdminApiError && error.status === 401) {
        logout();
        setNotice({ kind: 'error', text: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.' });
      }
      throw error;
    } finally {
      setSaving(false);
    }
  }

  async function removePoi(poi: AdminPoi) {
    if (!token || !window.confirm(`Bạn có chắc muốn xóa “${poi.name}”? Thao tác này không thể hoàn tác.`)) return;
    setNotice(null);
    try {
      await deleteAdminPoi(token, poi.id);
      setPois((current) => current.filter((item) => item.id !== poi.id));
      setNotice({ kind: 'success', text: `Đã xóa “${poi.name}”.` });
    } catch (error) {
      if (error instanceof AdminApiError && error.status === 401) {
        logout();
        setNotice({ kind: 'error', text: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.' });
      } else {
        setNotice({ kind: 'error', text: error instanceof Error ? error.message : 'Không thể xóa địa điểm.' });
      }
    }
  }

  if (!token || !admin) return <LoginScreen onLogin={handleLogin} />;

  return (
    <div className="admin-shell">
      {sidebarOpen && <button className="admin-sidebar-scrim" aria-label="Đóng menu" onClick={() => setSidebarOpen(false)} />}
      <aside className={`admin-sidebar ${sidebarOpen ? 'admin-sidebar-open' : ''}`}>
        <a className="admin-brand" href="/" aria-label="VoiceMap trang chủ"><span className="admin-brand-mark"><Icon name="pin" size={20} /></span><span>voice<span>map</span></span></a>
        <p className="admin-sidebar-label">KHÔNG GIAN LÀM VIỆC</p>
        <button className={`admin-nav-item ${activeSection === 'overview' ? 'admin-nav-active' : ''}`} onClick={() => { setActiveSection('overview'); setSidebarOpen(false); }}><Icon name="activity" /><span>Dashboard</span></button>
        <p className="admin-sidebar-label admin-sidebar-group-label">QUẢN LÝ NỘI DUNG</p>
        <button className={`admin-nav-item ${activeSection === 'places' ? 'admin-nav-active' : ''}`} onClick={() => { setActiveSection('places'); setSidebarOpen(false); }}><Icon name="pin" /><span>Địa điểm POI</span><span className="admin-nav-count">{poisLoaded ? pois.length : '—'}</span></button>
        <button className={`admin-nav-item ${activeSection === 'pipeline' ? 'admin-nav-active' : ''}`} onClick={() => { setActiveSection('pipeline'); setSidebarOpen(false); }}><Icon name="globe" /><span>Bản dịch & audio</span></button>
        {([['routes', 'Lộ trình gợi ý'], ['accounts', 'Tài khoản'], ['usage', 'Sự kiện sử dụng'], ['logs', 'Nhật ký hệ thống']] as const).map(([section, label]) => <button key={section} className={`admin-nav-item ${activeSection === section ? 'admin-nav-active' : ''}`} onClick={() => { setActiveSection(section); setSidebarOpen(false); }}><Icon name={section === 'accounts' ? 'shield' : 'activity'} /><span>{label}</span></button>)}
        <div className="admin-sidebar-bottom">
          <div className="admin-sidebar-tip"><span className="admin-tip-icon"><Icon name="shield" size={17} /></span><div><strong>Không gian an toàn</strong><span>Chỉ quản trị viên được cấp quyền mới có thể chỉnh sửa nội dung.</span></div></div>
          <a className="admin-nav-item" href="/" title="Mở trang dành cho khách"><Icon name="arrow" /><span>Trang khám phá</span></a>
          <button className="admin-nav-item admin-logout" onClick={logout}><Icon name="logout" /><span>Đăng xuất</span></button>
        </div>
      </aside>

      <main className="admin-main">
        <header className="admin-topbar">
          <button className="admin-icon-button admin-menu-toggle" aria-label="Mở menu" onClick={() => setSidebarOpen(true)}><Icon name="menu" /></button>
          <div className="admin-breadcrumb">Quản trị <span>/</span> <strong>{sectionDetails[activeSection].title}</strong></div>
          <div className="admin-account"><span className="admin-account-avatar">{(admin.fullName || admin.username).slice(0, 1).toUpperCase()}</span><span className="admin-account-name">{admin.fullName || admin.username}<small>Quản trị viên</small></span><button className="admin-icon-button admin-top-logout" aria-label="Đăng xuất" onClick={logout}><Icon name="logout" size={17} /></button></div>
        </header>

        <div className="admin-content">
          <section className="admin-welcome">
            <div><p className="admin-overline"><span className="admin-overline-dot" /> BẢNG ĐIỀU KHIỂN</p><h1>{sectionDetails[activeSection].title}</h1><p className="admin-page-description">{sectionDetails[activeSection].description}</p></div>
            {activeSection === 'places' && <button className="admin-button admin-button-primary admin-add-button" onClick={() => setModalPoi(null)}><Icon name="plus" size={18} /><span>Thêm địa điểm</span></button>}
          </section>

          {useAdminMockData && <div className="admin-demo-banner" role="status"><Icon name="shield" size={16} /><span><strong>Chế độ demo local</strong> — dữ liệu mẫu lưu trong trình duyệt, không kết nối database.</span></div>}

          {notice && <div className={`admin-toast ${notice.kind === 'error' ? 'admin-toast-error' : ''}`} role={notice.kind === 'error' ? 'alert' : 'status'}><span className="admin-toast-mark"><Icon name={notice.kind === 'success' ? 'check' : 'close'} size={15} /></span>{notice.text}<button className="admin-icon-button" aria-label="Đóng thông báo" onClick={() => setNotice(null)}><Icon name="close" size={15} /></button></div>}

          {activeSection === 'overview' && <AdminDashboard token={token} adminId={admin.id} />}

          {activeSection === 'places' && <>
            {poisError && <p role="alert">{poisError} {poisLoaded ? 'Đang giữ danh sách đọc gần nhất.' : 'Chưa xác định được số POI trong database.'}</p>}
            <section className="admin-stat-grid" aria-label="Thống kê địa điểm">
              <article className="admin-stat-card"><span className="admin-stat-icon admin-stat-blue"><Icon name="pin" size={19} /></span><span className="admin-stat-label">Tổng địa điểm</span><strong>{loading || !poisLoaded ? '—' : pois.length.toLocaleString('vi-VN')}</strong><span className="admin-stat-caption">POI trong hệ thống</span></article>
              <article className="admin-stat-card"><span className="admin-stat-icon admin-stat-green"><Icon name="globe" size={19} /></span><span className="admin-stat-label">Đã xuất bản</span><strong>{loading || !poisLoaded ? '—' : publishedCount.toLocaleString('vi-VN')}</strong><span className="admin-stat-caption">POI đã xuất bản</span></article>
              <article className="admin-stat-card"><span className="admin-stat-icon admin-stat-amber"><Icon name="grid" size={19} /></span><span className="admin-stat-label">Bản nháp & ẩn</span><strong>{loading || !poisLoaded ? '—' : hiddenCount.toLocaleString('vi-VN')}</strong><span className="admin-stat-caption">POI chưa xuất bản</span></article>
            </section>

            <section className="admin-insights" aria-label="Trạng thái xuất bản">
              <article className="admin-insight-card">
                <header className="admin-insight-heading">
                  <div><h2>Trạng thái POI</h2><p>Phân bố địa điểm đã xuất bản và bản nháp/ẩn.</p></div>
                  <span className="admin-insight-percent">{loading || !poisLoaded ? '—' : `${publishedPercent}%`}</span>
                </header>
                <div className="admin-status-bar" role="img" aria-label={`${publishedCount} đã xuất bản, ${hiddenCount} bản nháp hoặc ẩn`}>
                  <span className="admin-status-bar-published" style={{ width: `${publishedPercent}%` }} />
                  <span className="admin-status-bar-hidden" style={{ width: `${100 - publishedPercent}%` }} />
                </div>
                <div className="admin-insight-legend">
                  <span><i className="admin-legend-published" />Đã xuất bản <strong>{loading || !poisLoaded ? '—' : publishedCount}</strong></span>
                  <span><i className="admin-legend-hidden" />Bản nháp & ẩn <strong>{loading || !poisLoaded ? '—' : hiddenCount}</strong></span>
                </div>
                {!loading && !poisError && pois.length === 0 && <p className="admin-insight-empty">Chưa có POI.</p>}
              </article>
            </section>

            <section className="admin-table-card">
            <header className="admin-table-heading"><div><h2>Danh sách địa điểm</h2><p>Xem và quản lý nội dung trên bản đồ.</p></div><button className="admin-icon-button admin-refresh-button" aria-label="Tải lại danh sách" title="Tải lại danh sách" disabled={loading} onClick={() => { setLoading(true); void loadPois(token); }}><Icon name="refresh" size={17} /></button></header>
            <div className="admin-table-toolbar">
              <label className="admin-search"><Icon name="search" size={17} /><input aria-label="Tìm địa điểm" placeholder="Tìm theo tên hoặc mô tả…" value={query} onChange={(event) => setQuery(event.target.value)} /><kbd>⌘ K</kbd></label>
              <div className="admin-filter-tabs" role="group" aria-label="Lọc trạng thái">
                {([['all', 'Tất cả'], ['published', 'Đã xuất bản'], ['hidden', 'Đã ẩn']] as const).map(([value, label]) => <button key={value} className={filter === value ? 'admin-filter-active' : ''} onClick={() => setFilter(value)}>{label}<span>{!poisLoaded ? '—' : value === 'all' ? pois.length : value === 'published' ? publishedCount : hiddenCount}</span></button>)}
              </div>
            </div>

            <div className="admin-table-scroll">
              <table className="admin-table">
                <thead><tr><th>ĐỊA ĐIỂM</th><th>TỌA ĐỘ</th><th>NGÔN NGỮ</th><th>TRẠNG THÁI</th><th><span className="admin-sr-only">Thao tác</span></th></tr></thead>
                <tbody>
                  {loading ? <tr><td colSpan={5}><div className="admin-table-empty"><span className="admin-loading-spinner" />Đang tải danh sách địa điểm…</div></td></tr>
                    : poisError && !poisLoaded ? <tr><td colSpan={5}><p role="alert">Không tải được danh sách POI. Chưa xác định được số bản ghi.</p></td></tr> : visiblePois.length === 0 ? <tr><td colSpan={5}><div className="admin-table-empty"><span className="admin-empty-icon"><Icon name={query ? 'search' : 'pin'} size={22} /></span><strong>{query || filter !== 'all' ? 'Không tìm thấy địa điểm phù hợp' : 'Chưa có địa điểm nào'}</strong><span>{query || filter !== 'all' ? 'Thử thay đổi từ khóa hoặc bộ lọc.' : 'Bắt đầu bằng cách thêm POI đầu tiên vào bản đồ.'}</span>{!query && filter === 'all' && <button className="admin-button admin-button-primary" onClick={() => setModalPoi(null)}><Icon name="plus" size={16} />Thêm địa điểm đầu tiên</button>}</div></td></tr>
                      : visiblePois.map((poi) => <tr key={poi.id}>
                        <td><div className="admin-poi-cell">{poi.thumbnailUrl ? <img className="admin-poi-thumbnail" src={poi.thumbnailUrl} alt="" loading="lazy" /> : <span className="admin-poi-placeholder"><Icon name="pin" size={18} /></span>}<div className="admin-poi-details"><strong title={poi.name}>{poi.name}</strong><span>{poi.originalDescription || 'Chưa có mô tả'}</span></div></div></td>
                        <td><span className="admin-coordinate">{poi.latitude.toFixed(4)}, {poi.longitude.toFixed(4)}</span><small className="admin-table-secondary">Bán kính {poi.proximityRadius} m</small></td>
                        <td><span className="admin-language-pill"><Icon name="globe" size={13} />{poi.originalLanguage.toUpperCase()}</span></td>
                        <td><span className={`admin-status-pill ${poi.status === 'Published' ? 'admin-status-published' : 'admin-status-hidden'}`}><i />{poi.status === 'Published' ? 'Đã xuất bản' : 'Đã ẩn'}</span></td>
                        <td><div className="admin-row-actions"><button className="admin-icon-button" aria-label={`Chỉnh sửa ${poi.name}`} title="Chỉnh sửa" onClick={() => setModalPoi(poi)}><Icon name="edit" size={16} /></button><button className="admin-icon-button admin-delete-button" aria-label={`Xóa ${poi.name}`} title="Xóa" onClick={() => void removePoi(poi)}><Icon name="trash" size={16} /></button></div></td>
                      </tr>)}
                </tbody>
              </table>
            </div>
            <footer className="admin-table-footer"><span>Hiển thị <strong>{loading || !poisLoaded ? '—' : visiblePois.length}</strong> / <strong>{poisLoaded ? pois.length : '—'}</strong> địa điểm</span><span>Dữ liệu cập nhật trực tiếp</span></footer>
            </section>
          </>}

          {activeSection === 'pipeline' && <TranslationAudioPage pois={pois} token={token} />}
          {activeSection === 'routes' && <AdminRoutesPage token={token} pois={pois} />}
          {activeSection === 'accounts' && <AdminAccountsPage token={token} current={admin} onProfile={profile => { if (profile.status === 'Locked') logout(); else setAdmin(profile); }} />}
          {(activeSection === 'usage' || activeSection === 'logs') && <AdminHistoryPage key={activeSection} token={token} kind={activeSection} pois={pois} />}

          <footer className="admin-page-footer">© {new Date().getFullYear()} VoiceMap <span>•</span> Quản lý địa điểm văn hóa</footer>
        </div>
      </main>
      {modalPoi !== undefined && <PoiModal key={modalPoi?.id ?? 'new-poi'} poi={modalPoi} saving={saving} onClose={() => setModalPoi(undefined)} onSave={savePoi} />}
    </div>
  );
}
