import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
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
  return <article className="admin-monitoring-stat" data-empty={value === null}><span>{label}</span><strong>{formatNumber(value)}{value === null ? '' : suffix}</strong>{value === null && <small>Chưa thu thập / chưa hỗ trợ</small>}</article>;
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
  const [showFailures, setShowFailures] = useState(false);
  const pipeline = data.pipeline;
  const stageNames: Record<string, string> = { translation: 'Dịch nội dung', tts: 'Tạo giọng đọc (TTS)', upload: 'Upload / kiểm tra Supabase', persistence: 'Lưu dữ liệu', cache: 'Đọc / lưu cache audio' };
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
      <div className="admin-pipeline-reasons" data-state={pipeline.isBlocked === true ? 'blocked' : pipeline.isBlocked === false ? 'open' : 'unknown'} aria-label="Lý do trạng thái pipeline">
        <strong>{pipeline.isBlocked === true ? 'Đang chặn tạo nội dung mới' : pipeline.isBlocked === false ? 'Không có chặn tạo nội dung mới' : 'Chưa có thông tin chặn từ API'}</strong>
        {pipeline.reasons?.length ? <ul>{pipeline.reasons.map((reason) => <li key={reason.code}>
          {reason.message}{reason.retryAtUtc && <> Có thể kiểm tra lại sau {formatTime(reason.retryAtUtc)}.</>}
          {reason.requiresOperatorAction && ' Cần người vận hành kiểm tra cấu hình hoặc quyền dịch vụ.'}
        </li>)}</ul> : <p>API chưa cung cấp lý do cụ thể cho trạng thái pipeline.</p>}
        <p>Audio đã Ready vẫn có thể được sử dụng khi tạo nội dung mới bị chặn.</p>
      </div>
      <div className="admin-monitoring-stats">
        <MetricCard label="Tác vụ đang chờ" value={data.pipeline.queued} />
        <MetricCard label="Tác vụ đang xử lý" value={data.pipeline.processing} />
        <button type="button" className="admin-monitoring-stat admin-failed-trigger" data-has-errors={pipeline.failed > 0} aria-expanded={showFailures} aria-controls="admin-failed-jobs" onClick={() => setShowFailures((current) => !current)}>
          <span>Tác vụ lỗi</span><strong>{formatNumber(pipeline.failed)}</strong><small>{showFailures ? 'Ẩn chi tiết' : 'Xem chi tiết'}</small>
        </button>
        <MetricCard label="Tỷ lệ lỗi" value={data.pipeline.errorRatePercent} suffix="%" />
        <MetricCard label="Thời gian phản hồi TB" value={data.pipeline.avgResponseMs} suffix=" ms" />
      </div>
      <p className="admin-job-history-scope">{pipeline.history
        ? <>Lịch sử queue trong RAM từ {formatTime(pipeline.history.startedAtUtc)}: giữ tối đa {pipeline.history.maxCompletedJobs} tác vụ hoàn tất trong {pipeline.history.retentionMinutes} phút gần nhất; có thể ít hơn nếu đạt giới hạn. {pipeline.history.lostOnRestart && 'Mất lịch sử khi API khởi động lại.'} Bộ đếm gồm các lần Failed, Unavailable và QuotaExceeded; không phải tổng 24 giờ.</>
        : 'API chưa cung cấp phạm vi và thời hạn lưu lịch sử tác vụ.'}</p>
      {showFailures && <article id="admin-failed-jobs" className="admin-monitoring-panel admin-failed-jobs" aria-label="Chi tiết tác vụ lỗi">
        <header><h3>Lịch sử tác vụ lỗi</h3><span>Không tự thử lại tác vụ</span></header>
        {pipeline.failedJobs === undefined ? <div className="admin-monitoring-empty">API hiện tại chưa cung cấp chi tiết tác vụ. Cần cập nhật bản API có chức năng theo dõi lỗi.</div>
          : pipeline.failedJobs.length ? <ul className="admin-failed-job-list">{pipeline.failedJobs.map((job) => <li key={job.jobId}>
            <strong>{job.poiName ?? 'Chưa tải được tên POI'} · ID {job.poiId}</strong>
            <dl>
              <div><dt>Ngôn ngữ</dt><dd>{job.language}</dd></div><div><dt>Mã tác vụ</dt><dd>{job.jobId}</dd></div>
              <div><dt>Thời điểm lỗi</dt><dd>{job.failedAtUtc ? formatTime(job.failedAtUtc) : 'Chưa ghi nhận'}</dd></div>
              <div><dt>Bước thất bại</dt><dd>{job.stage ? stageNames[job.stage] ?? 'Chưa ghi nhận' : 'Chưa ghi nhận'}</dd></div>
              <div><dt>Mã lỗi</dt><dd className="admin-job-error-code" data-known={job.errorCode !== 'UnknownFailure'}>{job.errorCode === 'UnknownFailure' ? 'Chưa có mã lỗi đã phân loại' : job.errorCode}</dd></div>
            </dl><p>{job.message}</p>
            {job.retryAtUtc && <p>Mốc chờ của lần lỗi: {formatTime(job.retryAtUtc)}. Đây không bảo đảm dịch vụ đã phục hồi.</p>}
            {job.requiresOperatorAction && <p>Lần xử lý này yêu cầu người vận hành kiểm tra.</p>}
            {job.recoveredAtUtc && <p className="admin-job-recovered">Đã có lượt xử lý thành công cho cùng nội dung lúc {formatTime(job.recoveredAtUtc)}.</p>}
          </li>)}</ul> : <div className="admin-monitoring-empty">Không có tác vụ lỗi trong lịch sử queue đang được giữ.</div>}
      </article>}
      <article className="admin-monitoring-panel admin-dashboard-errors"><header><h3>Nhật ký lỗi hệ thống</h3><span>SQL · 24 giờ · nguồn riêng với queue</span></header>
        {data.pipeline.recentErrors.length ? <ul className="admin-dashboard-error-list">{data.pipeline.recentErrors.map((item, index) => <li key={`${item.occurredAtUtc}-${index}`}><span className="admin-dashboard-error-mark" aria-hidden="true">!</span><div><strong>{item.component}</strong><p>{item.message}</p></div><time>{formatTime(item.occurredAtUtc)}</time></li>)}</ul> : <div className="admin-monitoring-empty">{pipeline.logsAvailable === true
          ? 'Chưa ghi nhận bản ghi lỗi SQL trong 24 giờ qua. Lịch sử queue có phạm vi riêng và được xem ở “Tác vụ lỗi”.'
          : pipeline.logsAvailable === false ? 'Chưa đọc được nhật ký SQL. Không thể kết luận hệ thống không có lỗi.'
            : 'API chưa xác nhận việc thu thập nhật ký SQL. Chi tiết lỗi tác vụ được xem riêng ở “Tác vụ lỗi”.'}</div>}
      </article>
    </section>

    <section className="admin-monitoring-section">
      <header className="admin-monitoring-heading"><div><h2>Mức sử dụng</h2><p>Hoạt động trong 24 giờ gần nhất.</p></div></header>
      <p className="admin-job-history-scope">{data.usage.activeUsers === null ? 'API chưa cung cấp số khách đang hoạt động.' : `Khách hoạt động: mã khách ẩn danh có heartbeat trong ${data.usage.activeWindowSeconds ?? 60} giây; dữ liệu này nằm trong RAM và mất khi API khởi động lại. Lượt nghe và sử dụng lộ trình lấy từ SQL trong 24 giờ.`}</p>
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
