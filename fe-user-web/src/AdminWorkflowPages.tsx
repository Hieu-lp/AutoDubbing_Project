import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAudioPlayer } from './hooks/useAudioPlayer';
import { adminRequest, useAdminMockData, type AdminPoi } from './services/adminPoiService';
import { adminPreparationApi, contentKey, isReady, preparationKey, visitorApi } from './services/api';
import { PreparationClient, preparationMessage, type PreparationState } from './services/preparationClient';
import type { PoiContent, PreparationStatus } from './types';
import './admin-workflow.css';

const statusLabels: Record<PreparationStatus, string> = {
  Ready: 'Hoàn tất', Queued: 'Đang chờ', Processing: 'Đang xử lý', Missing: 'Chưa hoàn tất',
  QuotaExceeded: 'Đang chặn quota', QueueFull: 'Hàng đợi đầy', Unavailable: 'Chưa thể tạo nội dung',
  Failed: 'Có lỗi', Superseded: 'Nguồn đã thay đổi', Cancelled: 'Đã hủy',
};
const adminContentKey = (id: number, language: string) => ['admin-poi-content', id, language] as const;
function hasValidAudio(content: PoiContent | undefined, language: string): boolean {
  if (!isReady(content, language)) return false;
  try {
    const url = new URL(content.audio!.url);
    return url.protocol === 'https:' && !url.username && !url.password;
  } catch { return false; }
}
export function TranslationAudioPage({ pois, token }: { pois: AdminPoi[]; token: string }) {
  if (useAdminMockData) return <p className="workflow-empty" role="status">Bản dịch & audio cần API thật. Tắt VITE_USE_MOCK_DATA và đăng nhập bằng tài khoản Admin thật để sử dụng.</p>;
  return <StoredTranslationAudioPage pois={pois} token={token} />;
}
function StoredTranslationAudioPage({ pois, token }: { pois: AdminPoi[]; token: string }) {
  const [selectedPoiId, setSelectedPoiId] = useState<number | null>(null);
  const [language, setLanguage] = useState('en');
  const [requestingKey, setRequestingKey] = useState<string | null>(null);
  const [states, setStates] = useState<Record<string, PreparationState>>({});
  const queryClient = useQueryClient();
  const [preparation] = useState(() => new PreparationClient((key, state) => {
    setStates(current => ({ ...current, [key]: state }));
    const saved = state.item?.content;
    if (saved) {
      const queryKey = adminContentKey(saved.poiId, saved.requestedLanguage);
      // A GET started before SQL committed must not overwrite a completed job with stale content.
      void queryClient.cancelQueries({ queryKey, exact: true }).then(() => queryClient.setQueryData<PoiContent>(queryKey, saved));
      void queryClient.invalidateQueries({ queryKey: contentKey(saved.poiId, saved.requestedLanguage) });
    }
  }, adminPreparationApi(token)));
  const poi = pois.find(item => item.id === selectedPoiId) ?? pois.find(item => item.status === 'Published') ?? pois[0];
  const published = poi?.status === 'Published';
  const inventory = useQuery({ queryKey: ['admin-contents', poi?.id], enabled: !!poi,
    queryFn: ({ signal }) => adminRequest<{ translations: { id: number; language: string; status: string; updatedAtUtc: string }[];
      audios: { id: number; language: string; status: string; durationSeconds: number; url: string | null }[] }>(`/api/admin/pois/${poi!.id}/contents`, token, { signal }), refetchInterval: 10_000 });
  const key = preparationKey(poi?.id ?? 0, language);
  const languages = useQuery({ queryKey: ['languages'], queryFn: ({ signal }) => visitorApi.languages(signal) });
  const settings = useQuery({ queryKey: ['proximity-settings'], queryFn: ({ signal }) => visitorApi.settings(signal) });
  const content = useQuery({ queryKey: adminContentKey(poi?.id ?? 0, language),
    queryFn: ({ signal }) => adminPreparationApi(token).content(poi!.id, language, signal), enabled: !!poi, retry: false });
  const saved = poi ? content.data : undefined;
  const ready = hasValidAudio(saved, language);
  const player = useAudioPlayer(poi ? key : null, ready ? saved : saved && { ...saved, audio: null });
  const state = states[key];
  const waiting = state?.item?.status === 'Queued' || state?.item?.status === 'Processing';
  const busy = requestingKey === key || (waiting && !state?.message);
  const textReady = !!saved && !saved.isFallback && saved.actualLanguage === language && !!saved.text.trim();
  const invalidAudio = !ready && isReady(saved, language);
  const status = ready ? 'Ready' : invalidAudio || content.isError || state?.message ? 'Failed'
    : state?.item?.status === 'Ready' ? 'Missing' : state?.item?.status ?? 'Missing';
  const retryDate = state?.retryAt ? new Date(state.retryAt) : null;
  useEffect(() => {
    preparation.setInterest('selection', published ? [key] : []);
    return () => preparation.setInterest('selection', []);
  }, [preparation, published, key]);
  useEffect(() => {
    const seconds = settings.data?.pollAfterSeconds;
    if (seconds && Number.isFinite(seconds) && seconds > 0) preparation.pollAfterMs = seconds * 1000;
  }, [preparation, settings.data]);
  useEffect(() => () => preparation.cancelAll(), [preparation]);
  async function prepare() {
    if (!poi || !published || ready || busy || !saved) return;
    setRequestingKey(key);
    try { await preparation.prepareLanguage(poi.id, language, true); }
    finally { setRequestingKey(current => current === key ? null : current); }
  }
  return <section className="workflow-page" aria-label="Bản dịch và audio">
    <audio ref={player.ref} preload="none" aria-label="Audio thuyết minh đã lưu" />
    <div className="workflow-intro"><p>Đọc nội dung đã lưu và tạo phần còn thiếu qua pipeline hiện có.</p><span className="workflow-data-tag">SQL / API</span></div>
    <div className="workflow-editor-layout">
      <aside className="workflow-list-card">
        <div className="workflow-card-heading"><div><h2>Địa điểm</h2><p>{pois.length} POI</p></div></div>
        <div className="workflow-poi-list">
          {pois.map(item => <button type="button" key={item.id} aria-pressed={poi?.id === item.id} className={`workflow-poi-option ${poi?.id === item.id ? 'is-selected' : ''}`} onClick={() => setSelectedPoiId(item.id)}>
            <span className="workflow-poi-marker">{String(item.id).padStart(2, '0')}</span><span className="workflow-poi-copy"><strong>{item.name}</strong><small>{item.status === 'Published' ? 'Đã xuất bản' : 'Đang ẩn'}</small></span><span className="workflow-chevron">›</span>
          </button>)}
          {!pois.length && <div className="workflow-empty-small">Thêm POI trước khi tạo nội dung.</div>}
        </div>
      </aside>
      <div className="workflow-review-card">
        <div className="workflow-card-heading workflow-review-heading"><div><p className="admin-overline">BẢN THUYẾT MINH</p><h2>{poi?.name ?? 'Chọn một địa điểm'}</h2></div>{published && <span className={`workflow-status status-${status.toLowerCase()}`} role="status">{content.isPending ? 'Đang tải' : requestingKey === key && !state ? 'Đang gửi yêu cầu' : statusLabels[status]}</span>}</div>
        {poi ? <>
          <div className="workflow-source"><span>MÔ TẢ GỐC · {poi.originalLanguage.toUpperCase()}</span><p>{poi.originalDescription}</p></div>
          {!published && <p className="workflow-notice" role="status">Đang đọc POI ẩn bằng quyền Admin. Xuất bản POI trước khi tạo nội dung mới; khách không được đọc POI này.</p>}
          <>
            <div className="workflow-language-row"><label htmlFor="workflow-language">Ngôn ngữ đích</label><select id="workflow-language" value={language} disabled={!languages.data?.length} onChange={event => setLanguage(event.target.value)}>
              {!languages.data?.length && <option value={language}>Chưa tải được ngôn ngữ</option>}
              {languages.data?.map(item => <option key={item.code} value={item.code}>{item.displayName}</option>)}
            </select><button type="button" className="admin-button admin-button-secondary" disabled={content.isFetching} onClick={() => void content.refetch()}>Đọc lại dữ liệu</button></div>
            {languages.isError && <p className="workflow-notice" role="alert">Không tải được danh sách ngôn ngữ. <button type="button" onClick={() => void languages.refetch()}>Thử tải lại ngôn ngữ</button></p>}
            {content.isError && <p className="workflow-notice" role="alert">Không đọc được nội dung từ API. {saved ? 'Đang giữ kết quả đọc gần nhất.' : 'Chưa xác định được nội dung đã lưu; chưa gửi yêu cầu tạo.'}</p>}
            {saved?.isFallback && <p className="workflow-notice" role="status">Đang hiển thị bản gốc ({saved.actualLanguage}); chưa có bản dịch ({saved.requestedLanguage}). <button type="button" onClick={() => setLanguage(saved.actualLanguage)}>Xem/nghe bản gốc</button></p>}
            <label className="workflow-text-label" htmlFor="workflow-narration">Nội dung đã lưu · {saved?.actualLanguage ?? language}</label>
            <textarea id="workflow-narration" className="workflow-textarea" value={saved?.text ?? ''} readOnly placeholder={content.isPending ? 'Đang đọc từ SQL qua API…' : 'Chưa đọc được nội dung.'} rows={9} />
            <div className="workflow-review-footer"><div className="workflow-status-caption">{ready ? 'Nội dung và audio đã sẵn sàng.' : textReady ? 'Đã có văn bản; còn thiếu audio hợp lệ.' : 'Chưa có nội dung hoàn chỉnh cho ngôn ngữ đã chọn.'}</div><div className="workflow-actions">
              <button type="button" className="admin-button admin-button-secondary" disabled={!ready} onClick={() => { if (ready && saved) { if (player.isPlaying) player.pause(); else void player.play(saved); } }}>{player.isPlaying ? 'Tạm dừng' : 'Phát audio đã lưu'}</button>
              <button type="button" className="admin-button admin-button-primary" disabled={!published || ready || invalidAudio || busy || !saved || content.isError || content.isFetching || !languages.data?.some(item => item.code === language) || state?.item?.requiresOperatorAction} onClick={() => void prepare()}>{busy ? 'Đang chuẩn bị…' : textReady ? 'Tạo audio còn thiếu' : 'Chuẩn bị nội dung & audio'}</button>
            </div></div>
            {!ready && state && <p className="workflow-notice" role="status">{preparationMessage(state)} {state.item?.errorCode && <code>{state.item.errorCode}</code>}{state.item?.jobId && <> · Mã tác vụ: {state.item.jobId}</>}{retryDate && Number.isFinite(retryDate.getTime()) && <> · Có thể thử lại từ {retryDate.toLocaleString('vi-VN')}</>}</p>}
            {invalidAudio && <p className="workflow-notice" role="alert">URL audio không hợp lệ; không phát file này. Cần kiểm tra dữ liệu ở backend.</p>}
            {player.error && <p className="workflow-notice" role="alert">{player.error}</p>}
            <p className="workflow-data-note">Chỉ nút tạo phần còn thiếu mới gửi yêu cầu sinh nội dung, có thể dùng quota dịch/TTS. Đọc lại dữ liệu và tải lại trang chỉ đọc kết quả đã lưu. Văn bản có sẵn được dùng lại; audio hợp lệ không được sinh lại. Nếu đang tạm dừng tạo nội dung, tải lại trang sau khi cấu hình backend được bật lại.</p>
          </>
          <div className="workflow-source"><h3>Nội dung và audio trong SQL</h3>
            {inventory.isPending ? <p>Đang đọc danh sách…</p> : inventory.isError ? <p role="alert">Không đọc được danh sách quản trị; kiểm tra phiên đăng nhập và phiên bản API.</p> : <>
              {inventory.data?.translations?.map(item => <p key={`text-${item.id}`}>Bản dịch #{item.id} · {item.language} · {item.status} · {item.updatedAtUtc}</p>)}
              {inventory.data?.audios?.map(item => <p key={`audio-${item.id}`}>Audio #{item.id} · {item.language} · {item.status} · {item.durationSeconds > 0 ? `${item.durationSeconds} giây` : 'Chưa ghi nhận thời lượng'}</p>)}
              {inventory.data?.translations?.length === 0 && inventory.data?.audios?.length === 0 && <p>Chưa có bản dịch hoặc audio trong SQL.</p>}
            </>}
          </div>
          <p className="workflow-data-note">Chưa có nghiệp vụ sửa/duyệt thủ công. Trang này chỉ đọc và chuẩn bị nội dung; sửa nguồn POI sẽ đánh dấu bản dịch/audio cũ cần cập nhật, giữ nguyên file trên storage.</p>
        </> : <div className="workflow-empty">Thêm POI trong mục Địa điểm POI để bắt đầu pipeline.</div>}
      </div>
    </div>
  </section>;
}
