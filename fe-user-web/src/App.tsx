import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTourGuide } from './hooks/useTourGuide';
import { visitorApi, contentKey } from './services/api';
import type { PoiContent } from './types';
import { useTranslation } from 'react-i18next';
import { Circle, MapContainer, Marker, Polyline, TileLayer, useMap, useMapEvents } from 'react-leaflet';
import L from 'leaflet';
import type { Coordinates, Language, VisitorLocation } from './types';
import { useVisitorData } from './hooks/useVisitorData';
import { useAudioPlayer } from './hooks/useAudioPlayer';
import { isReady, preparationKey } from './services/api';
import { preparationMessage } from './services/preparationClient';
import { distanceMeters } from './tour/proximity';
import { createWalkingRoute, type WalkingRoute } from './services/routeService';

const initialCenter: Coordinates = { latitude: 10.7769, longitude: 106.7009 };
const locationIcon = L.divIcon({ className: 'location-marker', html: '<span></span>', iconSize: [24, 24], iconAnchor: [12, 12] });
const poiIcon = (number: number | string, selected: boolean) => L.divIcon({ className: 'poi-marker-wrap', html: `<span class="poi-marker ${selected ? 'poi-marker-selected' : ''}">${number}</span>`, iconSize: [34, 42], iconAnchor: [17, 38] });

function formatDistance(distance: number, english: boolean) {
  return distance < 1000 ? `${Math.round(distance)} m` : `${(distance / 1000).toFixed(1)} ${english ? 'km' : 'km'}`;
}

function MapClickHandler({ onChoose, active }: { onChoose: (point: Coordinates) => void; active: boolean }) {
  useMapEvents({ click(event) { if (active) onChoose({ latitude: event.latlng.lat, longitude: event.latlng.lng }); } });
  return null;
}

function MapLocation({ point }: { point: Coordinates | null }) {
  const map = useMap(); const latitude = point?.latitude; const longitude = point?.longitude;
  useEffect(() => { if (latitude !== undefined && longitude !== undefined) map.panTo([latitude,longitude]); }, [map,latitude,longitude]);
  return null;
}

function FitRoute({ points }: { points: [number, number][] }) {
  const map = useMap();
  useEffect(() => {
    if (points.length > 1) map.fitBounds(points, { padding: [48, 48], maxZoom: 16 });
  }, [map, points]);
  return null;
}

function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const common = { width: size, height: size, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, 'aria-hidden': true as const };
  if (name === 'pin') return <svg {...common}><path d="M20 10c0 5-8 12-8 12S4 15 4 10a8 8 0 1 1 16 0Z"/><circle cx="12" cy="10" r="2.5"/></svg>;
  if (name === 'compass') return <svg {...common}><circle cx="12" cy="12" r="9"/><path d="m15.5 8.5-2.2 4.8-4.8 2.2 2.2-4.8 4.8-2.2Z"/></svg>;
  if (name === 'arrow') return <svg {...common}><path d="M7 17 17 7M7 7h10v10"/></svg>;
  if (name === 'volume') return <svg {...common}><path d="M11 5 6 9H3v6h3l5 4V5Z"/><path d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13"/></svg>;
  if (name === 'play') return <svg {...common} fill="currentColor" stroke="none"><path d="m8 5 11 7-11 7V5Z"/></svg>;
  if (name === 'pause') return <svg {...common} fill="currentColor" stroke="none"><path d="M7 5h4v14H7zM15 5h4v14h-4z"/></svg>;
  if (name === 'back') return <svg {...common}><path d="m15 18-6-6 6-6"/></svg>;
  if (name === 'close') return <svg {...common}><path d="m18 6-12 12M6 6l12 12"/></svg>;
  if (name === 'route') return <svg {...common}><circle cx="6" cy="18" r="2"/><circle cx="18" cy="6" r="2"/><path d="M8 18h2a4 4 0 0 0 4-4v-4a4 4 0 0 1 4-4"/></svg>;
  return <svg {...common}><circle cx="12" cy="12" r="9"/><path d="M12 8v4l3 2"/></svg>;
}

export default function App() {
  const { t, i18n } = useTranslation();
  const [location, setLocation] = useState<VisitorLocation | null>(null);
  const locationSource = location?.source ?? null;
  const [gpsState, setGpsState] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  const [gpsMessage, setGpsMessage] = useState('');
  const [selectedPoiId, setSelectedPoiId] = useState<number | null>(null);
  const [language, setLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'vi');
  const [narrationLanguage, setNarrationLanguage] = useState<Language>('vi');
  const [mapPicker, setMapPicker] = useState(false);
  const [routeMode, setRouteMode] = useState(false);
  const [routePoiIds, setRoutePoiIds] = useState<number[]>([]);
  const [walkingRoute, setWalkingRoute] = useState<WalkingRoute | null>(null);
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeError, setRouteError] = useState('');
  const [playIntent, setPlayIntent] = useState<string | null>(null);
  const selectedPoiRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();
  const [pageVisible, setPageVisible] = useState(!document.hidden);
  const routeRequest = useRef<{ version: number; controller?: AbortController }>({ version: 0 });
  const choiceVersion = useRef(0);
  const selection = selectedPoiId !== null && !routeMode ? preparationKey(selectedPoiId, narrationLanguage) : null;
  const selectionRef = useRef(selection);
  useLayoutEffect(() => { selectionRef.current = selection; }, [selection]);
  const visitor = useVisitorData(selectedPoiId, narrationLanguage, !routeMode && pageVisible);
  const pois = visitor.pois.data ?? [];
  const narrationLanguages = visitor.languages.data ?? [];
  const selectedPoi = visitor.detail.data ?? pois.find(poi => poi.id === selectedPoiId) ?? null;
  const selectedContent = visitor.content.data;
  const audio = useAudioPlayer(selection, selectedContent);
  const { isPlaying, currentTime, duration, error: audioError } = audio;
  const audioRef = audio.ref;
  const selectedPreparation = selection ? visitor.preparations[selection] : undefined;
  const nearby = [...pois].sort((a,b) => location ? distanceMeters(location,a)-distanceMeters(location,b) || a.id-b.id : a.id-b.id);
  const routeOrder = walkingRoute?.stops.map(stop => stop.poiId) ?? routePoiIds;
  const routePois = routeOrder.map(id => pois.find(poi => poi.id === id)).filter((poi): poi is (typeof pois)[number] => !!poi);
  const mapPois = pois;
  const nearbyNotices = location && visitor.settings.data ? nearby
    .filter(poi => distanceMeters(location, poi) <= visitor.settings.data!.preparationRadiusMeters)
    .map(poi => ({ poi, message: preparationMessage(visitor.preparations[preparationKey(poi.id, poi.originalLanguage)]) }))
    .filter(item => item.message) : [];
  const guide = useTourGuide({
    pois, settings: visitor.settings.data, preparation: visitor.preparation,
    selectedId: selectedPoiId, language: narrationLanguage, routeMode,
    getCached: (id,code) => queryClient.getQueryData<PoiContent>(contentKey(id,code)),
    readSource: async (poi,signal) => {
      const cached = queryClient.getQueryData<PoiContent>(contentKey(poi.id,poi.originalLanguage));
      if (isReady(cached,poi.originalLanguage)) return cached;
      const saved = await visitorApi.content(poi.id,poi.originalLanguage,signal);
      if (!signal.aborted) queryClient.setQueryData(contentKey(poi.id,poi.originalLanguage),saved);
      return saved;
    },
    onLocation: point => { setLocation(point); },
    onGpsState: (state,message) => { setGpsState(state); setGpsMessage(message); },
    onAutoSelect: poi => { setSelectedPoiId(poi.id); setNarrationLanguage(poi.originalLanguage); setPlayIntent(null); },
    player: audio,
  });
  const playAudio = audio.play;
  const notePlayed = guide.notePlayed;
  useEffect(() => {
    const change = () => setPageVisible(!document.hidden);
    document.addEventListener('visibilitychange',change);
    return () => document.removeEventListener('visibilitychange',change);
  }, []);


  useEffect(() => { if (selectedPoiId) selectedPoiRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, [selectedPoiId]);
  useEffect(() => {
    if (playIntent !== selection || !playIntent || !isReady(selectedContent, narrationLanguage)) return;
    const version = choiceVersion.current;
    setPlayIntent(null);
    void playAudio(selectedContent, () => version === choiceVersion.current && selectionRef.current === selection)
      .then(success => { if (success) notePlayed(selectedContent.poiId); });
  }, [playIntent, selection, selectedContent, narrationLanguage, playAudio, notePlayed]);
  useEffect(() => () => { routeRequest.current.controller?.abort(); }, []);

  function invalidateRoute() {
    routeRequest.current.controller?.abort(); routeRequest.current.version++;
    setWalkingRoute(null); setRouteError(''); setRouteLoading(false);
  }
  function locateMe() {
    choiceVersion.current++; setPlayIntent(null); setMapPicker(false); invalidateRoute(); guide.locate();
  }
  function toggleNarration() {
    choiceVersion.current++; setPlayIntent(null);
    if (guide.running) guide.stop();
    else { audioRef.current?.load(); guide.start(); }
  }
  function chooseMapPoint(point: Coordinates) {
    setSelectedPoiId(null); setRouteMode(false);
    choiceVersion.current++; setPlayIntent(null); guide.chooseMapPoint(point);
    setMapPicker(false); invalidateRoute();
  }
  function selectPoi(id: number) { guide.invalidate(); choiceVersion.current++; setPlayIntent(null); audio.stop(); setSelectedPoiId(id); }
  function clearSelection() { guide.invalidate(); choiceVersion.current++; setPlayIntent(null); audio.stop(); setSelectedPoiId(null); }
  function toggleRoutePoi(id: number) {
    invalidateRoute(); setRoutePoiIds(current => current.includes(id) ? current.filter(item => item !== id) : [...current,id]);
  }
  async function buildRoute() {
    if (!location) { setRouteError('Hãy chọn vị trí xuất phát trên bản đồ hoặc dùng vị trí GPS.'); return; }
    if (routePois.length < 2) { setRouteError('Chọn ít nhất 2 POI để tạo lộ trình.'); return; }
    routeRequest.current.controller?.abort();
    const controller = new AbortController(); const version = ++routeRequest.current.version;
    routeRequest.current.controller = controller; setRouteLoading(true); setRouteError('');
    try {
      const result = await createWalkingRoute(location, routePois, controller.signal);
      if (version === routeRequest.current.version && !controller.signal.aborted) setWalkingRoute(result);
    } catch (failure) {
      if (version === routeRequest.current.version && !controller.signal.aborted) {
        setWalkingRoute(null); setRouteError(failure instanceof Error ? failure.message : 'Không lấy được tuyến đi bộ.');
      }
    } finally { if (version === routeRequest.current.version) setRouteLoading(false); }
  }
  function toggleRouteMode() { clearSelection(); invalidateRoute(); setRouteMode(current => !current); }
  async function toggleAudio() {
    if (audio.isPlaying) { audio.pause(); return; }
    if (isReady(selectedContent, narrationLanguage)) {
      const key = selection; const version = choiceVersion.current;
      const success = await audio.play(selectedContent, () => version === choiceVersion.current && selectionRef.current === key);
      if (success) guide.notePlayed(selectedContent.poiId);
    } else if (selectedPoiId !== null) {
      setPlayIntent(selection); await visitor.preparation.prepareLanguage(selectedPoiId,narrationLanguage);
    }
  }
  function changeNarrationLanguage(next: Language) {
    guide.invalidate(); choiceVersion.current++; audio.stop(); setNarrationLanguage(next);
    setPlayIntent(selectedPoiId !== null ? preparationKey(selectedPoiId,next) : null);
  }
  function changeLanguage(next: Language) { setLanguage(next); void i18n.changeLanguage(next); }
  function skip(seconds: number) { audio.seek(currentTime+seconds); }
  async function refreshContent() {
    const result = await visitor.content.refetch();
    if (selectedPoiId !== null && result.data && !isReady(result.data,narrationLanguage))
      await visitor.preparation.prepareLanguage(selectedPoiId,narrationLanguage,true);
  }
  const timeText = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

  return (
    <main className="app-shell">
      <audio ref={audioRef} preload="metadata" data-testid="narration-audio" />
      <div className="ambient ambient-one" /><div className="ambient ambient-two" />
      <div className="experience">
        <header className="site-header">
          <a className="brand" href="#top" aria-label="VoiceMap home"><span className="brand-mark"><Icon name="volume" size={19} /></span><span>voice<span className="brand-accent">map</span></span></a>
          <div className="header-right"><span className="header-caption">SAIGON, VIETNAM</span><div className="language-switch" aria-label="Interface language"><button className={language === 'vi' ? 'active' : ''} onClick={() => changeLanguage('vi')}>VI</button><span /><button className={language === 'en' ? 'active' : ''} onClick={() => changeLanguage('en')}>EN</button></div></div>
        </header>

        <section className="intro" id="top">
          <div className="eyebrow"><span className="eyebrow-line" /> A CITY THAT SPEAKS</div>
          <div className="intro-row"><div><h1>{t('discover')}<span className="title-period">.</span></h1><p>{t('subtitle')}</p></div><div className="intro-decoration" aria-hidden="true"><span>10°</span><i>✳</i></div></div>
        </section>

        <section className="explorer" aria-label={t('discover')}>
          <div className="map-panel">
            <MapContainer center={[location?.latitude ?? initialCenter.latitude, location?.longitude ?? initialCenter.longitude]} zoom={15} scrollWheelZoom className="leaflet-map">
              <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap contributors</a>' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
              <MapClickHandler onChoose={chooseMapPoint} active={mapPicker} />
              <MapLocation point={location} />
              {walkingRoute && <><Polyline positions={walkingRoute.points} pathOptions={{ color: '#2563eb', weight: 5, opacity: 0.86, lineCap: 'round', lineJoin: 'round' }} /><FitRoute points={walkingRoute.points} /></>}
              {location && visitor.settings.data && <Circle interactive={false} center={[location.latitude,location.longitude]} radius={visitor.settings.data.preparationRadiusMeters} pathOptions={{ color: '#2563eb', weight: 1, fillOpacity: .05 }} />}
              {location && <Marker position={[location.latitude, location.longitude]} icon={locationIcon} bubblingMouseEvents={false} />}
              {mapPois.map((poi, index) => {
                const stopNumber = routeOrder.indexOf(poi.id);
                return <Marker title={poi.name} alt={poi.name} key={poi.id} position={[poi.latitude, poi.longitude]} icon={poiIcon(routeMode ? (stopNumber >= 0 ? stopNumber + 1 : '·') : index + 1, routeMode ? stopNumber >= 0 : selectedPoiId === poi.id)} bubblingMouseEvents={false} eventHandlers={{ click: () => routeMode ? toggleRoutePoi(poi.id) : selectPoi(poi.id) }} />;
              })}
            </MapContainer>
            <div className="map-topline"><div className="map-location"><span className={`status-dot ${locationSource === 'gps' ? 'status-live' : ''}`} />{locationSource === 'gps' ? t('gps') : locationSource === 'map' ? t('selected') : 'QUẬN 1 · TP. HỒ CHÍ MINH'}</div><button className="map-locate" onClick={locateMe} aria-label={t('locate')} title={t('locate')}><Icon name="compass" size={19} /></button></div>
            {mapPicker && <div className="map-instruction"><span className="instruction-icon"><Icon name="pin" size={17} /></span>{language === 'vi' ? 'Chạm vào bản đồ để chọn vị trí' : 'Tap the map to choose a location'}<button onClick={() => setMapPicker(false)} aria-label="Close"><Icon name="close" size={17} /></button></div>}
            {location && visitor.settings.data && <div className="radius-label"><span />{visitor.settings.data.preparationRadiusMeters} m · {language === 'vi' ? 'vùng chuẩn bị' : 'preparation area'}</div>}
            {!location && <div className="map-empty-card"><span className="empty-pin"><Icon name="pin" size={20} /></span><b>{language === 'vi' ? 'Bắt đầu từ vị trí của bạn' : 'Start from your location'}</b><span>{language === 'vi' ? 'Bật GPS hoặc chọn một điểm trên bản đồ.' : 'Turn on GPS or choose a point on the map.'}</span></div>}
            <div className="map-credit">{walkingRoute?.attribution ?? '© OpenStreetMap contributors'}</div>
          </div>

          <div className="places-panel">
            <div className="places-heading"><div><div className="eyebrow places-eyebrow"><span className="eyebrow-line" /> {routeMode ? (language === 'vi' ? 'ĐI BỘ QUA CÁC ĐIỂM' : 'WALKING ITINERARY') : 'YOUR NEIGHBORHOOD'}</div><h2>{routeMode ? (language === 'vi' ? 'Lộ trình' : 'Your route') : selectedPoi ? t('listen') : t('near')}<span className="title-period">.</span></h2></div><span className="place-count">{routeMode ? String(routePoiIds.length).padStart(2, '0') : String(pois.length).padStart(2, '0')}</span></div>
            <div className="places-actions"><button className="primary-action" onClick={locateMe} disabled={gpsState === 'loading'}><Icon name="compass" size={18} />{gpsState === 'loading' ? t('loading') : t('locate')}</button><button className={`secondary-action ${mapPicker ? 'selected-action' : ''}`} onClick={() => setMapPicker(!mapPicker)}><Icon name="pin" size={17} />{t('choose')}</button><button className={`route-action ${routeMode ? 'route-action-active' : ''}`} onClick={toggleRouteMode} aria-pressed={routeMode}><Icon name="route" size={17} />{routeMode ? (language === 'vi' ? 'Đang chọn điểm' : 'Selecting stops') : (language === 'vi' ? 'Tạo lộ trình' : 'Plan route')}</button></div>
            <button className={`narration-action ${guide.running ? 'narration-active' : ''}`} onClick={toggleNarration} disabled={!visitor.settings.data || !pois.length} aria-pressed={guide.running}>
              <Icon name="volume" size={18} />{guide.running ? (language === 'vi' ? 'Dừng thuyết minh' : 'Stop narration') : (language === 'vi' ? 'Bắt đầu thuyết minh' : 'Start narration')}
            </button>
            {guide.running && <p className="integration-note" role="status">{guide.reliable ? `${locationSource === 'map' ? 'Dùng vị trí đã chọn' : 'GPS đang bật'} · chuẩn bị ${visitor.settings.data?.preparationRadiusMeters} m · tự phát ${visitor.settings.data?.playbackRadiusMeters} m / ${visitor.settings.data?.dwellSeconds} giây` : 'Đang chờ vị trí GPS mới và đủ tin cậy; chưa tự phát audio.'}</p>}
            {guide.running && nearbyNotices.map(({ poi, message }) => <p key={poi.id} className="integration-note" role="status">{poi.name}: {message}</p>)}
            {gpsMessage && <div className={`notice ${gpsState === 'ready' ? 'notice-soft' : ''}`} role="status"><span>{gpsState === 'ready' ? 'i' : '!'}</span><p>{gpsMessage}</p><button onClick={() => setGpsMessage('')} aria-label="Dismiss"><Icon name="close" size={16} /></button></div>}
            {visitor.pois.isPending && <p className="integration-note" role="status">Đang tải địa điểm…</p>}
            {visitor.pois.isError && <div className="route-error" role="alert">Không tải được danh sách địa điểm. <button onClick={() => void visitor.pois.refetch()}>Thử lại</button></div>}
            {(visitor.languages.isError || visitor.settings.isError) && <p className="integration-note" role="alert">Chưa tải được ngôn ngữ/cấu hình thuyết minh. <button onClick={() => { void visitor.languages.refetch(); void visitor.settings.refetch(); }}>Thử lại</button></p>}
            {routeMode ? (
              <div className="route-builder">
                <div className="route-origin"><span className="route-origin-dot" /><span>{language === 'vi' ? `Bắt đầu: ${locationSource === 'gps' ? 'vị trí GPS của bạn' : locationSource === 'map' ? 'vị trí đã chọn trên bản đồ' : 'chưa chọn vị trí xuất phát'}` : `Start: ${locationSource === 'gps' ? 'your GPS location' : locationSource === 'map' ? 'selected map point' : 'choose a starting location'}`}</span></div>
                <p className="route-hint">{language === 'vi' ? 'Chọn từ 2 điểm trở lên. Backend sẽ sắp xếp thứ tự ghé theo điểm gần nhất.' : 'Choose at least 2 places. The backend orders the stops by nearest place.'}</p>
                <div className="route-stop-list" aria-label={language === 'vi' ? 'Các điểm trong lộ trình' : 'Route stops'}>
                  {routePois.map((poi, index) => <div className="route-stop" key={poi.id}><span className="route-stop-number">{String(index + 1).padStart(2, '0')}</span><span className="route-stop-name">{poi.name}</span><button onClick={() => toggleRoutePoi(poi.id)} aria-label={language === 'vi' ? `Bỏ ${poi.name} khỏi lộ trình` : `Remove ${poi.name} from route`}><Icon name="close" size={15} /></button></div>)}
                </div>
                <div className="route-poi-picker"><div className="route-picker-title">{language === 'vi' ? 'CHỌN ĐIỂM THAM QUAN' : 'CHOOSE PLACES'}</div>{pois.map((poi) => { const included = routePoiIds.includes(poi.id); return <button type="button" key={poi.id} className={`route-choice ${included ? 'route-choice-selected' : ''}`} onClick={() => toggleRoutePoi(poi.id)} aria-pressed={included}><span className="route-check">{included ? '✓' : '+'}</span><span>{poi.name}</span></button>; })}</div>
                {walkingRoute && <div className="route-result" role="status"><b>{language === 'vi' ? 'Đã tạo tuyến đi bộ' : 'Walking route ready'}</b><span>{formatDistance(walkingRoute.distanceMeters, language === 'en')} · {Math.max(1, Math.round(walkingRoute.durationSeconds / 60))} {language === 'vi' ? 'phút đi bộ' : 'min walk'}</span><small>{walkingRoute.attribution}</small></div>}
                {routeError && <div className="route-error" role="alert">{routeError}</div>}
                <button className="route-submit" onClick={() => void buildRoute()} disabled={routeLoading || routePoiIds.length < 2}><Icon name="route" size={17} />{routeLoading ? (language === 'vi' ? 'Đang tính đường…' : 'Finding route…') : walkingRoute ? (language === 'vi' ? 'Cập nhật lộ trình' : 'Update route') : (language === 'vi' ? `Tạo lộ trình (${routePoiIds.length})` : `Create route (${routePoiIds.length})`)}</button>
                {!walkingRoute && !routeError && routePoiIds.length < 2 && <small className="route-validation">{language === 'vi' ? 'Chọn thêm ít nhất một điểm nữa.' : 'Choose at least one more place.'}</small>}
              </div>
              ) : selectedPoi ? (
              <div className="detail-card" ref={selectedPoiRef}>
                <button className="back-link" onClick={clearSelection}><Icon name="back" size={17} />{t('back')}</button>
                <div className="detail-image" style={{ backgroundImage: `linear-gradient(180deg, transparent 30%, rgba(24, 34, 30, .64)), url("${selectedPoi.thumbnailUrl ?? ''}")` }}><span className="image-category">{selectedPoi.category ?? 'Điểm tham quan'}</span><div className="image-title">{selectedPoi.name}</div></div>
                <div className="detail-meta"><span><Icon name="pin" size={14} />{language === 'vi' ? 'Điểm tham quan' : 'Place to explore'}</span><label>{language === 'vi' ? 'NGÔN NGỮ THUYẾT MINH' : 'NARRATION LANGUAGE'}<select aria-label="Ngôn ngữ thuyết minh" disabled={!narrationLanguages.length} value={narrationLanguage} onChange={(event) => changeNarrationLanguage(event.target.value as Language)}>{narrationLanguages.map((item) => <option key={item.code} value={item.code}>{item.displayName}</option>)}</select></label></div>
                {visitor.content.isFetching && <p className="integration-note" role="status">Đang tải nội dung…</p>}
                {(visitor.content.isError || visitor.detail.isError) && <p className="route-error" role="alert">Không tải được nội dung. <button onClick={() => void refreshContent()}>Thử lại</button></p>}
                {selectedContent?.isFallback && <div className="fallback-note" role="status">{language === 'vi' ? 'Đang hiển thị bản gốc' : 'Showing original content'} ({selectedContent.actualLanguage}); {language === 'vi' ? 'chưa có bản dịch' : 'translation unavailable'} ({selectedContent.requestedLanguage}). {selectedContent.audio && <button onClick={() => changeNarrationLanguage(selectedContent.actualLanguage as Language)}>Nghe bản gốc</button>}</div>}
                {preparationMessage(selectedPreparation) && <p className="integration-note" role="status">{preparationMessage(selectedPreparation)}</p>}

                <div className="audio-player"><input aria-label={language === 'vi' ? 'Tiến độ audio' : 'Audio progress'} type="range" min={0} max={duration || 100} value={duration ? currentTime : 0} onChange={(event) => { if (audioRef.current && duration) audio.seek(Number(event.target.value)); }} style={{ '--progress': `${duration ? (currentTime / duration) * 100 : 0}%` } as React.CSSProperties} /><div className="audio-times"><span>{timeText(currentTime)}</span><span>{duration ? timeText(duration) : '—:—'}</span></div><div className="audio-controls"><button className="skip-button" onClick={() => skip(-10)} aria-label="Back 10 seconds"><span>−10</span></button><button className="play-button" onClick={() => void toggleAudio()} aria-label={isPlaying ? 'Tạm dừng' : 'Phát'}><Icon name={isPlaying ? 'pause' : 'play'} size={22} /></button><button className="skip-button" onClick={() => skip(10)} aria-label="Forward 10 seconds"><span>+10</span></button><button className="speed-button" onClick={audio.cycleSpeed}>{audio.speed}×</button></div>{audioError && <div className="audio-note" role="status">{audioError}</div>}</div>
                <div className="transcript"><div className="transcript-heading"><span className="transcript-icon"><Icon name="volume" size={15} /></span><span>{t('read')}</span><i /></div><p>{selectedContent?.text ?? (visitor.content.isPending ? 'Đang tải nội dung…' : visitor.detail.data?.originalDescription ?? '')}</p>{selectedContent && !isReady(selectedContent,narrationLanguage) && <div className="audio-note">Nội dung hiện chưa sẵn sàng <button onClick={() => void refreshContent()}>Kiểm tra lại nội dung</button></div>}</div>
              </div>
            ) : (
              <>
                <div className="nearby-summary"><span className="summary-icon"><Icon name="pin" size={15} /></span><span>{nearby.length} {language === 'vi' ? 'địa điểm' : 'places'}</span><span className="summary-dot">·</span><span>{language === 'vi' ? 'Điểm tham quan tại Quận 1' : 'Places in District 1'}</span><button className="route-inline-link" onClick={toggleRouteMode}>{language === 'vi' ? 'Chọn nhiều điểm →' : 'Select multiple →'}</button></div>
                <div className="poi-list" aria-label="Danh sách POI">{nearby.map((poi, index) => <button className="poi-row" key={poi.id} onClick={() => selectPoi(poi.id)}><span className="poi-index">{String(index + 1).padStart(2, '0')}</span><span className="poi-row-body"><span className="poi-name">{poi.name}</span><span className="poi-category">{poi.category ?? (language === 'vi' ? 'Điểm tham quan' : 'Place to explore')}</span></span><span className="poi-distance" title="Khoảng cách gần đúng theo vị trí">{location ? formatDistance(distanceMeters(location,poi),language === 'en') : ''}</span><span className="poi-arrow"><Icon name="arrow" size={16} /></span></button>)}</div>
                <div className="arrive-tip"><span className="arrive-icon"><Icon name="volume" size={16} /></span><p>{language === 'vi' ? 'Chạm vào một địa điểm để xem câu chuyện.' : 'Tap a place to explore its story.'}</p><span className="tip-wave"><i /><i /><i /><i /><i /></span></div>
              </>
            )}
          </div>
        </section>

        <section className="explore-footer"><div className="footer-star">✳</div><p>{language === 'vi' ? 'Đi chậm thôi. Thành phố có nhiều điều để kể.' : 'Take your time. The city has stories to tell.'}</p><span>HO CHI MINH CITY · 2026</span></section>
      </div>
    </main>
  );
}
