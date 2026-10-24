import { useEffect, useMemo, useState, type FormEvent } from 'react';
import type { AdminPoi } from './services/adminPoiService';
import './admin-workflow.css';
import {
  formatUpdatedAt,
  loadNarrations,
  loadRoutes,
  loadSessions,
  narrationLanguages,
  saveNarrations,
  saveRoutes,
  saveSessions,
  workflowChangeEvent,
  type DemoRoute,
  type DemoSession,
  type NarrationDraft,
} from './adminWorkflowData';

function useWorkflowRefresh() {
  const [, setVersion] = useState(0);
  useEffect(() => {
    const refresh = () => setVersion((value) => value + 1);
    window.addEventListener(workflowChangeEvent, refresh);
    window.addEventListener('storage', refresh);
    return () => {
      window.removeEventListener(workflowChangeEvent, refresh);
      window.removeEventListener('storage', refresh);
    };
  }, []);
}

function statusLabel(status: string) {
  if (status === 'approved') return 'Đã duyệt';
  if (status === 'review') return 'Chờ duyệt';
  if (status === 'published') return 'Đã xuất bản';
  if (status === 'closed') return 'Đã đóng';
  return 'Bản nháp';
}

function makeId() {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

export function TranslationAudioPage({ pois }: { pois: AdminPoi[] }) {
  const [selectedPoiId, setSelectedPoiId] = useState(pois[0]?.id ?? 0);
  const [language, setLanguage] = useState<string>('en');
  const [narrations, setNarrations] = useState<NarrationDraft[]>(loadNarrations);
  const [notice, setNotice] = useState('');
  const poi = pois.find((item) => item.id === selectedPoiId);
  const narration = narrations.find((item) => item.poiId === selectedPoiId && item.language === language);
  const languageLabel = narrationLanguages.find((item) => item.code === language)?.label ?? language;
  const [text, setText] = useState(narration?.text ?? '');

  useEffect(() => {
    setText(narration?.text ?? '');
    setNotice('');
  }, [selectedPoiId, language]);

  function persist(next: NarrationDraft[]) {
    setNarrations(next);
    if (!saveNarrations(next)) setNotice('Không lưu được dữ liệu demo trong trình duyệt.');
  }

  function generateDemo() {
    if (!poi) return;
    const generated: NarrationDraft = {
      poiId: poi.id,
      language,
      text: `【Bản dịch demo · ${languageLabel}】\n${poi.originalDescription}\n\nHãy biên tập nội dung này trước khi duyệt.`,
      status: 'review',
      updatedAt: new Date().toISOString(),
    };
    const next = [...narrations.filter((item) => !(item.poiId === poi.id && item.language === language)), generated];
    persist(next);
    setText(generated.text);
    setNotice('Đã tạo bản nội dung minh họa. Đây chưa phải bản dịch thật.');
  }

  function saveDraft(status: NarrationDraft['status']) {
    if (!poi || !text.trim()) {
      setNotice('Nhập nội dung trước khi lưu.');
      return;
    }
    const draft: NarrationDraft = { poiId: poi.id, language, text: text.trim(), status, updatedAt: new Date().toISOString() };
    persist([...narrations.filter((item) => !(item.poiId === poi.id && item.language === language)), draft]);
    setNotice(status === 'approved' ? 'Đã duyệt bản thuyết minh demo.' : 'Đã lưu bản nháp và chuyển sang chờ duyệt.');
  }

  function previewSpeech() {
    if (!text.trim() || !('speechSynthesis' in window)) {
      setNotice('Trình duyệt này chưa hỗ trợ phát giọng đọc thử.');
      return;
    }
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = language;
    window.speechSynthesis.speak(utterance);
    setNotice('Đang phát thử bằng giọng đọc có sẵn của trình duyệt; chưa tạo file audio.');
  }

  return (
    <section className="workflow-page">
      <div className="workflow-intro"><div><p>Biên tập và duyệt nội dung theo từng POI, từng ngôn ngữ.</p></div><span className="workflow-demo-tag">MÔ PHỎNG LOCAL</span></div>
      <div className="workflow-editor-layout">
        <aside className="workflow-list-card">
          <div className="workflow-card-heading"><div><h2>Địa điểm</h2><p>{pois.length} POI</p></div></div>
          <div className="workflow-poi-list">
            {pois.map((item) => {
              const itemRecords = narrations.filter((entry) => entry.poiId === item.id);
              const itemPending = itemRecords.filter((entry) => entry.status === 'review').length;
              return <button type="button" key={item.id} className={`workflow-poi-option ${selectedPoiId === item.id ? 'is-selected' : ''}`} onClick={() => setSelectedPoiId(item.id)}>
                <span className="workflow-poi-marker">{String(item.id).padStart(2, '0')}</span><span className="workflow-poi-copy"><strong>{item.name}</strong><small>{itemRecords.length} ngôn ngữ · {itemPending ? `${itemPending} chờ duyệt` : 'chưa có mục chờ duyệt'}</small></span><span className="workflow-chevron">›</span>
              </button>;
            })}
            {!pois.length && <div className="workflow-empty-small">Thêm POI trước khi tạo nội dung.</div>}
          </div>
        </aside>

        <div className="workflow-review-card">
          <div className="workflow-card-heading workflow-review-heading"><div><p className="admin-overline">BẢN THUYẾT MINH</p><h2>{poi?.name ?? 'Chọn một địa điểm'}</h2></div>{narration && <span className={`workflow-status status-${narration.status}`}>{statusLabel(narration.status)}</span>}</div>
          {poi ? <>
            <div className="workflow-source"><span>MÔ TẢ GỐC · {poi.originalLanguage.toUpperCase()}</span><p>{poi.originalDescription}</p></div>
            <div className="workflow-language-row"><label htmlFor="workflow-language">Ngôn ngữ đích</label><select id="workflow-language" value={language} onChange={(event) => setLanguage(event.target.value)}>{narrationLanguages.map((item) => <option key={item.code} value={item.code}>{item.label}</option>)}</select><button type="button" className="admin-button admin-button-secondary" onClick={generateDemo}>Tạo bản demo</button></div>
            <label className="workflow-text-label" htmlFor="workflow-narration">Nội dung biên dịch và thuyết minh</label>
            <textarea id="workflow-narration" className="workflow-textarea" value={text} onChange={(event) => setText(event.target.value)} placeholder="Tạo bản demo hoặc nhập nội dung cần duyệt…" rows={9} />
            <div className="workflow-review-footer"><div className="workflow-status-caption">{narration ? `Cập nhật ${formatUpdatedAt(narration.updatedAt)}` : 'Chưa có nội dung cho ngôn ngữ này'}</div><div className="workflow-actions"><button type="button" className="admin-button admin-button-secondary" onClick={previewSpeech} disabled={!text.trim()}>Nghe thử</button><button type="button" className="admin-button admin-button-secondary" onClick={() => saveDraft('review')} disabled={!text.trim()}>Lưu chờ duyệt</button><button type="button" className="admin-button admin-button-primary" onClick={() => saveDraft('approved')} disabled={!text.trim()}>Duyệt nội dung</button></div></div>
            {notice && <p className="workflow-notice" role="status">{notice}</p>}
            <p className="workflow-demo-note">Bản dịch và audio chỉ mô phỏng trên máy này. “Nghe thử” dùng giọng đọc của trình duyệt, không tạo file audio.</p>
          </> : <div className="workflow-empty">Thêm POI trong mục Địa điểm POI để bắt đầu pipeline.</div>}
        </div>
      </div>
    </section>
  );
}

export function RoutesPage({ pois }: { pois: AdminPoi[] }) {
  const [routes, setRoutes] = useState<DemoRoute[]>(loadRoutes);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [poiIds, setPoiIds] = useState<number[]>([]);
  const [error, setError] = useState('');

  function startNew() { setEditingId('new'); setName(''); setPoiIds([]); setError(''); }
  function startEdit(route: DemoRoute) { setEditingId(route.id); setName(route.name); setPoiIds(route.poiIds); setError(''); }
  function cancelEdit() { setEditingId(null); setError(''); }
  function togglePoi(id: number) { setPoiIds((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]); }
  function moveStop(index: number, delta: -1 | 1) {
    setPoiIds((current) => {
      const next = [...current]; const target = index + delta;
      if (target < 0 || target >= next.length) return current;
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  function saveRoute(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (poiIds.length < 2) { setError('Chọn ít nhất 2 POI theo thứ tự tham quan.'); return; }
    const previous = routes.find((item) => item.id === editingId);
    const route: DemoRoute = { id: editingId && editingId !== 'new' ? editingId : makeId(), name: name.trim(), poiIds, status: previous?.status ?? 'draft', updatedAt: new Date().toISOString() };
    const next = previous ? routes.map((item) => item.id === route.id ? route : item) : [...routes, route];
    if (!saveRoutes(next)) { setError('Không lưu được dữ liệu demo trong trình duyệt.'); return; }
    setRoutes(next); setEditingId(null); setError('');
  }

  function deleteRoute(route: DemoRoute) {
    if (!window.confirm(`Xóa lộ trình “${route.name}” khỏi dữ liệu demo?`)) return;
    const next = routes.filter((item) => item.id !== route.id);
    if (saveRoutes(next)) setRoutes(next);
  }

  function publishRoute(route: DemoRoute, status: DemoRoute['status']) {
    const next = routes.map((item) => item.id === route.id ? { ...item, status, updatedAt: new Date().toISOString() } : item);
    if (saveRoutes(next)) setRoutes(next);
  }

  const selectedStops = useMemo(() => poiIds.map((id) => pois.find((poi) => poi.id === id)).filter((poi): poi is AdminPoi => Boolean(poi)), [poiIds, pois]);

  return (
    <section className="workflow-page">
      <div className="workflow-intro"><p>Tạo tuyến tham quan bằng cách chọn POI và sắp xếp thứ tự dừng.</p><span className="workflow-demo-tag">MÔ PHỎNG LOCAL</span></div>
      <div className="workflow-toolbar"><div><h2>Lộ trình đã lưu</h2><p>{routes.length} lộ trình trong trình duyệt này</p></div><button type="button" className="admin-button admin-button-primary" onClick={editingId ? cancelEdit : startNew}>{editingId ? 'Đóng trình tạo' : '+ Tạo lộ trình'}</button></div>
      {editingId && <form className="workflow-builder-card" onSubmit={saveRoute}>
        <div className="workflow-card-heading"><div><p className="admin-overline">THIẾT LẬP LỘ TRÌNH</p><h2>{editingId === 'new' ? 'Lộ trình mới' : 'Chỉnh sửa lộ trình'}</h2></div><button type="button" className="admin-icon-button" aria-label="Đóng" onClick={cancelEdit}>×</button></div>
        <label className="workflow-form-field">Tên lộ trình<input value={name} onChange={(event) => setName(event.target.value)} maxLength={120} required placeholder="Ví dụ: Dạo quanh trung tâm Sài Gòn" /></label>
        <div className="workflow-route-columns"><div><h3>Chọn POI</h3><div className="workflow-check-list">{pois.map((poi) => <label key={poi.id} className="workflow-check-row"><input type="checkbox" checked={poiIds.includes(poi.id)} onChange={() => togglePoi(poi.id)} /><span><strong>{poi.name}</strong><small>{poi.status === 'Published' ? 'Đã xuất bản' : 'Đang ẩn'}</small></span></label>)}</div></div>
          <div><h3>Thứ tự tham quan <span>{selectedStops.length} điểm</span></h3><ol className="workflow-stop-list">{selectedStops.map((poi, index) => <li key={poi.id}><span className="workflow-stop-index">{String(index + 1).padStart(2, '0')}</span><span className="workflow-stop-name">{poi.name}</span><button type="button" aria-label={`Đưa ${poi.name} lên`} disabled={index === 0} onClick={() => moveStop(index, -1)}>↑</button><button type="button" aria-label={`Đưa ${poi.name} xuống`} disabled={index === selectedStops.length - 1} onClick={() => moveStop(index, 1)}>↓</button></li>)}</ol></div>
        </div>
        {error && <p className="workflow-form-error" role="alert">{error}</p>}
        <div className="workflow-actions"><button type="button" className="admin-button admin-button-secondary" onClick={cancelEdit}>Hủy</button><button type="submit" className="admin-button admin-button-primary">Lưu lộ trình nháp</button></div>
      </form>}
      <div className="workflow-record-list">{routes.map((route) => <article className="workflow-record-card" key={route.id}>
        <div className="workflow-record-icon route-record-icon"><span>↗</span></div><div className="workflow-record-main"><div className="workflow-record-title"><h3>{route.name}</h3><span className={`workflow-status status-${route.status}`}>{statusLabel(route.status)}</span></div><p>{route.poiIds.map((id) => pois.find((poi) => poi.id === id)?.name).filter(Boolean).join(' → ') || 'Các POI đã chọn'}</p><small>{route.poiIds.length} điểm dừng · Cập nhật {formatUpdatedAt(route.updatedAt)}</small></div>
        <div className="workflow-record-actions"><button type="button" className="admin-button admin-button-secondary" onClick={() => startEdit(route)}>Sửa</button><button type="button" className="admin-button admin-button-secondary" onClick={() => publishRoute(route, route.status === 'published' ? 'draft' : 'published')}>{route.status === 'published' ? 'Gỡ xuất bản' : 'Xuất bản'}</button><button type="button" className="workflow-delete-button" onClick={() => deleteRoute(route)} aria-label={`Xóa lộ trình ${route.name}`}>Xóa</button></div>
      </article>)}{!routes.length && !editingId && <div className="workflow-empty-card"><span>01</span><strong>Chưa có lộ trình nào</strong><p>Tạo lộ trình đầu tiên bằng cách chọn và sắp xếp các POI.</p><button className="admin-button admin-button-primary" onClick={startNew}>Tạo lộ trình</button></div>}</div>
    </section>
  );
}

export function SessionsPage({ pois }: { pois: AdminPoi[] }) {
  useWorkflowRefresh();
  const [sessions, setSessions] = useState<DemoSession[]>(loadSessions);
  const [routes, setRoutes] = useState<DemoRoute[]>(loadRoutes);
  const [narrations, setNarrations] = useState<NarrationDraft[]>(loadNarrations);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [routeIds, setRouteIds] = useState<string[]>([]);
  const [error, setError] = useState('');

  useEffect(() => {
    const refresh = () => { setRoutes(loadRoutes()); setNarrations(loadNarrations()); };
    window.addEventListener(workflowChangeEvent, refresh);
    return () => window.removeEventListener(workflowChangeEvent, refresh);
  }, []);

  function resetForm() { setEditingId(null); setName(''); setDescription(''); setRouteIds([]); setError(''); }
  function startNew() { setEditingId('new'); setName(''); setDescription(''); setRouteIds([]); setError(''); }
  function startEdit(session: DemoSession) { setEditingId(session.id); setName(session.name); setDescription(session.description); setRouteIds(session.routeIds); setError(''); }
  function toggleRoute(id: string) { setRouteIds((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]); }

  function saveSession(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (routeIds.length === 0) { setError('Chọn ít nhất một lộ trình cho session.'); return; }
    const previous = sessions.find((item) => item.id === editingId);
    const session: DemoSession = { id: editingId && editingId !== 'new' ? editingId : makeId(), name: name.trim(), description: description.trim(), routeIds, status: previous?.status ?? 'draft', updatedAt: new Date().toISOString() };
    const next = previous ? sessions.map((item) => item.id === session.id ? session : item) : [...sessions, session];
    if (!saveSessions(next)) { setError('Không lưu được dữ liệu demo trong trình duyệt.'); return; }
    setSessions(next); resetForm();
  }

  function updateSession(session: DemoSession, status: DemoSession['status']) {
    const next = sessions.map((item) => item.id === session.id ? { ...item, status, updatedAt: new Date().toISOString() } : item);
    if (saveSessions(next)) setSessions(next);
  }

  function canPublish(session: DemoSession) {
    const includedPoiIds = routes.filter((route) => session.routeIds.includes(route.id)).flatMap((route) => route.poiIds);
    return includedPoiIds.length > 0 && includedPoiIds.every((id) => narrations.some((item) => item.poiId === id && item.status === 'approved'));
  }

  return (
    <section className="workflow-page">
      <div className="workflow-intro"><p>Gom các lộ trình thành session tham quan, sau đó xuất bản hoặc đóng.</p><span className="workflow-demo-tag">MÔ PHỎNG LOCAL</span></div>
      <div className="workflow-toolbar"><div><h2>Session tham quan</h2><p>{sessions.length} session · {sessions.filter((item) => item.status === 'published').length} đang xuất bản</p></div><button type="button" className="admin-button admin-button-primary" onClick={editingId ? resetForm : startNew}>{editingId ? 'Đóng trình tạo' : '+ Tạo session'}</button></div>
      {editingId && <form className="workflow-builder-card session-builder-card" onSubmit={saveSession}>
        <div className="workflow-card-heading"><div><p className="admin-overline">THÔNG TIN SESSION</p><h2>{editingId === 'new' ? 'Session mới' : 'Chỉnh sửa session'}</h2></div><button type="button" className="admin-icon-button" aria-label="Đóng" onClick={resetForm}>×</button></div>
        <label className="workflow-form-field">Tên session<input value={name} onChange={(event) => setName(event.target.value)} maxLength={120} required placeholder="Ví dụ: Sài Gòn qua những công trình xưa" /></label>
        <label className="workflow-form-field">Mô tả<textarea value={description} onChange={(event) => setDescription(event.target.value)} rows={3} maxLength={1000} placeholder="Mô tả ngắn cho chương trình tham quan" /></label>
        <fieldset className="workflow-route-selection"><legend>Chọn lộ trình</legend>{routes.map((route) => <label key={route.id} className="workflow-check-row"><input type="checkbox" checked={routeIds.includes(route.id)} onChange={() => toggleRoute(route.id)} /><span><strong>{route.name}</strong><small>{route.poiIds.length} điểm · {statusLabel(route.status)}</small></span></label>)}{!routes.length && <p className="workflow-form-error">Tạo lộ trình trước khi tạo session.</p>}</fieldset>
        {error && <p className="workflow-form-error" role="alert">{error}</p>}
        <div className="workflow-actions"><button type="button" className="admin-button admin-button-secondary" onClick={resetForm}>Hủy</button><button type="submit" className="admin-button admin-button-primary" disabled={!routes.length}>Lưu session nháp</button></div>
      </form>}
      <div className="workflow-record-list">{sessions.map((session) => {
        const sessionRoutes = routes.filter((route) => session.routeIds.includes(route.id));
        const stopCount = sessionRoutes.reduce((sum, route) => sum + route.poiIds.length, 0);
        const ready = canPublish(session);
        const uniquePoiIds = [...new Set(sessionRoutes.flatMap((route) => route.poiIds))];
        const missingPoiNames = uniquePoiIds.filter((id) => !narrations.some((item) => item.poiId === id && item.status === 'approved')).map((id) => pois.find((poi) => poi.id === id)?.name ?? `POI #${id}`);
        return <article className="workflow-record-card" key={session.id}>
          <div className="workflow-record-icon session-record-icon"><span>◎</span></div><div className="workflow-record-main"><div className="workflow-record-title"><h3>{session.name}</h3><span className={`workflow-status status-${session.status}`}>{statusLabel(session.status)}</span></div><p>{session.description || 'Chưa có mô tả.'}</p><small>{sessionRoutes.length} lộ trình · {stopCount} lượt dừng · Cập nhật {formatUpdatedAt(session.updatedAt)}</small>{session.status === 'draft' && !ready && <small className="workflow-prerequisite">Cần duyệt ít nhất một bản thuyết minh cho: {missingPoiNames.join(', ') || 'các POI đã chọn'}.</small>}</div>
          <div className="workflow-record-actions">{session.status === 'draft' && <button type="button" className="admin-button admin-button-primary" disabled={!ready} title={!ready ? 'Duyệt nội dung cho các POI trong session trước' : undefined} onClick={() => updateSession(session, 'published')}>Xuất bản</button>}{session.status === 'published' && <button type="button" className="admin-button admin-button-secondary" onClick={() => updateSession(session, 'closed')}>Đóng session</button>}{session.status === 'closed' && <button type="button" className="admin-button admin-button-secondary" onClick={() => updateSession(session, 'draft')}>Mở lại nháp</button>}{session.status !== 'closed' && <button type="button" className="admin-button admin-button-secondary" onClick={() => startEdit(session)}>Sửa</button>}</div>
        </article>;
      })}{!sessions.length && !editingId && <div className="workflow-empty-card"><span>01</span><strong>Chưa có session nào</strong><p>Tạo lộ trình trước, sau đó gom các tuyến vào session tham quan.</p><button className="admin-button admin-button-primary" onClick={startNew}>Tạo session</button></div>}</div>
      <p className="workflow-demo-note">Xuất bản chỉ đổi trạng thái trong dữ liệu demo local; chưa gửi nội dung ra hệ thống thật.</p>
    </section>
  );
}
