import { useQuery } from '@tanstack/react-query';
import { getAdminDashboard, useAdminMockData, type AdminDashboardResponse, type DashboardHealth } from './services/adminPoiService';
import './admin-dashboard.css';

const healthLabels: Record<DashboardHealth, string> = {
  online: 'Bình thường',
  warning: 'Cần kiểm tra',
  offline: 'Không hoạt động',
  unknown: 'Chưa rõ',
};

function formatNumber(value: number | null) {
  return value === null ? '—' : new Intl.NumberFormat('vi-VN').format(value);
}

function formatTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Chưa có thời gian cập nhật' : new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'medium' }).format(date);
}

function HealthCard({ name, value }: { name: string; value: DashboardHealth }) {
  return <article className="admin-health-card"><span className={`admin-health-dot health-${value}`} /><div><strong>{name}</strong><span>{healthLabels[value]}</span></div></article>;
}

function MetricCard({ label, value, suffix = '' }: { label: string; value: number | null; suffix?: string }) {
  return <article className="admin-monitoring-stat"><span>{label}</span><strong>{formatNumber(value)}{value === null ? '' : suffix}</strong></article>;
}

function Breakdown({ title, items }: { title: string; items: { label: string; count: number }[] }) {
  const maximum = Math.max(1, ...items.map((item) => item.count));
  return <article className="admin-monitoring-panel">
    <header><h3>{title}</h3><span>Trong 24 giờ</span></header>
    {items.length ? <ul className="admin-dashboard-breakdown">{items.map((item) => <li key={item.label}>
      <span className="admin-dashboard-breakdown-name" title={item.label}>{item.label}</span><span className="admin-dashboard-meter" role="img" aria-label={`${item.label}: ${formatNumber(item.count)} lượt`}><i style={{ width: `${Math.max(4, (item.count / maximum) * 100)}%` }} /></span><strong>{formatNumber(item.count)}</strong>
    </li>)}</ul> : <div className="admin-monitoring-empty">Chưa có dữ liệu.</div>}
  </article>;
}

function DashboardContent({ data }: { data: AdminDashboardResponse }) {
  const playTotal = (data.usage.autoPlayCount ?? 0) + (data.usage.manualPlayCount ?? 0);
  const autoPlayPercent = data.usage.autoPlayCount === null || data.usage.manualPlayCount === null
    ? null : playTotal ? Math.round((data.usage.autoPlayCount / playTotal) * 100) : 0;
  return <>
    <section className="admin-monitoring-section">
      <header className="admin-monitoring-heading"><div><h2>Tình trạng hệ thống</h2><p>Trạng thái các thành phần chính và công việc xử lý.</p></div><span className="admin-monitoring-unavailable">Cập nhật {formatTime(data.generatedAtUtc)}</span></header>
      <div className="admin-health-grid">
        <HealthCard name="Ứng dụng" value={data.health.api} />
        <HealthCard name="Cơ sở dữ liệu" value={data.health.database} />
        <HealthCard name="Pipeline" value={data.health.pipeline} />
      </div>
      <div className="admin-monitoring-stats">
        <MetricCard label="Tác vụ đang chờ" value={data.pipeline.queued} />
        <MetricCard label="Tác vụ đang xử lý" value={data.pipeline.processing} />
        <MetricCard label="Tác vụ lỗi" value={data.pipeline.failed} />
        <MetricCard label="Tỷ lệ lỗi" value={data.pipeline.errorRatePercent} suffix="%" />
        <MetricCard label="Thời gian phản hồi TB" value={data.pipeline.avgResponseMs} suffix=" ms" />
      </div>
      <article className="admin-monitoring-panel admin-dashboard-errors"><header><h3>Lỗi gần đây</h3><span>Nhật ký hệ thống</span></header>
        {data.pipeline.recentErrors.length ? <ul className="admin-dashboard-error-list">{data.pipeline.recentErrors.map((item, index) => <li key={`${item.occurredAtUtc}-${index}`}><span className="admin-dashboard-error-mark" aria-hidden="true">!</span><div><strong>{item.component}</strong><p>{item.message}</p></div><time>{formatTime(item.occurredAtUtc)}</time></li>)}</ul> : <div className="admin-monitoring-empty">Không có lỗi gần đây.</div>}
      </article>
    </section>

    <section className="admin-monitoring-section">
      <header className="admin-monitoring-heading"><div><h2>Mức sử dụng</h2><p>Hoạt động trong 24 giờ gần nhất.</p></div></header>
      <div className="admin-monitoring-stats admin-usage-stats">
        <MetricCard label="Người dùng đang hoạt động" value={data.usage.activeUsers} />
        <MetricCard label="Lượt nghe tự động" value={data.usage.autoPlayCount} />
        <MetricCard label="Lượt nghe do chạm POI" value={data.usage.manualPlayCount} />
        <MetricCard label="Tỷ lệ nghe tự động" value={autoPlayPercent} suffix="%" />
        <MetricCard label="Lộ trình phổ biến nhất" value={data.usage.topRoute?.selections ?? null} />
      </div>
      {data.usage.topRoute && <p className="admin-dashboard-top-route">Được chọn nhiều nhất: <strong>{data.usage.topRoute.name}</strong></p>}
      <div className="admin-monitoring-panels">
        <Breakdown title="Lượt nghe theo POI" items={data.usage.listensByPoi.map((item) => ({ label: item.name, count: item.count }))} />
        <Breakdown title="Lượt nghe theo ngôn ngữ" items={data.usage.listensByLanguage.map((item) => ({ label: item.language.toUpperCase(), count: item.count }))} />
      </div>
    </section>
  </>;
}

export default function AdminDashboard({ token, adminId }: { token: string; adminId: number }) {
  const query = useQuery({
    queryKey: ['admin-dashboard', adminId],
    queryFn: () => getAdminDashboard(token),
    enabled: Boolean(token),
    staleTime: 5_000,
    refetchInterval: 10_000,
  });

  return <section className="admin-monitoring" aria-label="Dashboard giám sát">
    <div className="admin-monitoring-note"><span>{useAdminMockData ? 'Đang hiển thị dữ liệu demo; dashboard tự làm mới mỗi 10 giây.' : 'Dashboard lấy dữ liệu API và tự làm mới mỗi 10 giây.'}</span><button className="admin-button admin-button-secondary admin-dashboard-refresh" type="button" onClick={() => void query.refetch()} disabled={query.isFetching}>{query.isFetching ? 'Đang tải…' : 'Làm mới'}</button></div>
    {query.isError && <div className="admin-dashboard-error-banner" role="alert">{query.data ? 'Không lấy được bản cập nhật mới; đang giữ số liệu gần nhất.' : query.error instanceof Error ? query.error.message : 'Không tải được dữ liệu Dashboard.'}<button type="button" onClick={() => void query.refetch()}>Thử lại</button></div>}
    {query.isLoading && <div className="admin-dashboard-state" role="status">Đang tải số liệu Dashboard…</div>}
    {!query.isLoading && !query.data && !query.isError && <div className="admin-dashboard-state">Chưa có dữ liệu Dashboard.</div>}
    {query.data && <DashboardContent data={query.data} />}
  </section>;
}
