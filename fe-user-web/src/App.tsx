import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MapContainer, Marker, Polyline, TileLayer, useMap, useMapEvents } from 'react-leaflet';
import L from 'leaflet';
import type { Coordinates, Language } from './types';
import { demoPois } from './data/mockPois';
import { createWalkingRoute, type WalkingRoute } from './services/routeService';

const demoCenter: Coordinates = { latitude: 10.7769, longitude: 106.7009 };
const narrationLanguages: { code: Language; label: string }[] = [
  { code: 'vi', label: 'Tiếng Việt' }, { code: 'en', label: 'English' },
  { code: 'zh-hans', label: '中文（简体）' }, { code: 'ja', label: '日本語' },
  { code: 'ko', label: '한국어' }, { code: 'fr', label: 'Français' },
  { code: 'de', label: 'Deutsch' }, { code: 'es', label: 'Español' },
  { code: 'ru', label: 'Русский' }, { code: 'it', label: 'Italiano' },
  { code: 'pt', label: 'Português (Brasil)' }, { code: 'th', label: 'ไทย' },
  { code: 'id', label: 'Bahasa Indonesia' }, { code: 'ms', label: 'Bahasa Melayu' },
  { code: 'hi', label: 'हिन्दी' },
];
const locationIcon = L.divIcon({ className: 'location-marker', html: '<span></span>', iconSize: [24, 24], iconAnchor: [12, 12] });
const poiIcon = (number: number | string, selected: boolean) => L.divIcon({ className: 'poi-marker-wrap', html: `<span class="poi-marker ${selected ? 'poi-marker-selected' : ''}">${number}</span>`, iconSize: [34, 42], iconAnchor: [17, 38] });

function formatDistance(distance: number, english: boolean) {
  return distance < 1000 ? `${Math.round(distance)} m` : `${(distance / 1000).toFixed(1)} ${english ? 'km' : 'km'}`;
}

function MapClickHandler({ onChoose }: { onChoose: (point: Coordinates) => void }) {
  useMapEvents({ click(event) { onChoose({ latitude: event.latlng.lat, longitude: event.latlng.lng }); } });
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
  const pois = demoPois;
  const [location, setLocation] = useState<Coordinates | null>(null);
  const [locationSource, setLocationSource] = useState<'gps' | 'map' | 'demo' | null>(null);
  const [gpsState, setGpsState] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  const [gpsMessage, setGpsMessage] = useState('');
  const [selectedPoiId, setSelectedPoiId] = useState<number | null>(null);
  const [language, setLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'vi');
  const [narrationLanguage, setNarrationLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'vi');
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [audioError, setAudioError] = useState('');
  const [mapPicker, setMapPicker] = useState(false);
  const [routeMode, setRouteMode] = useState(false);
  const [routePoiIds, setRoutePoiIds] = useState<number[]>([]);
  const [walkingRoute, setWalkingRoute] = useState<WalkingRoute | null>(null);
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeError, setRouteError] = useState('');
  const audioRef = useRef<HTMLAudioElement>(null);
  const selectedPoiRef = useRef<HTMLDivElement>(null);
  const gpsWatchRef = useRef<number | null>(null);
  const gpsTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const nearby = location ? pois : [];
  const routePois = routePoiIds.map((id) => pois.find((poi) => poi.id === id)).filter((poi): poi is (typeof pois)[number] => Boolean(poi));
  const mapPois = routeMode ? pois : nearby;
  const selectedPoi = pois.find((poi) => poi.id === selectedPoiId) ?? null;
  const selectedContent = selectedPoi ? {
    text: selectedPoi.descriptions?.[narrationLanguage] ?? selectedPoi.originalDescription ?? '',
    audioUrl: selectedPoi.audioUrls?.[narrationLanguage] ?? null,
    actualLanguage: narrationLanguage,
    isFallback: false,
  } : undefined;

  useEffect(() => { if (selectedPoiId) selectedPoiRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, [selectedPoiId]);
  useEffect(() => {
    const player = audioRef.current;
    if (!player) return;
    const update = () => setCurrentTime(player.currentTime || 0);
    const loaded = () => { setDuration(player.duration || 0); setAudioError(''); };
    const ended = () => setIsPlaying(false);
    player.addEventListener('timeupdate', update); player.addEventListener('loadedmetadata', loaded); player.addEventListener('ended', ended);
    return () => { player.removeEventListener('timeupdate', update); player.removeEventListener('loadedmetadata', loaded); player.removeEventListener('ended', ended); };
  }, [selectedPoiId, narrationLanguage]);

  useEffect(() => () => {
    if (gpsWatchRef.current !== null) navigator.geolocation?.clearWatch(gpsWatchRef.current);
    if (gpsTimeoutRef.current) clearTimeout(gpsTimeoutRef.current);
  }, []);


  function locateMe() {
    setGpsMessage(''); setGpsState('loading');
    if (!window.isSecureContext) {
      setGpsState('error');
      setGpsMessage(language === 'vi' ? 'Trình duyệt chỉ cho GPS trên HTTPS hoặc localhost. Hãy mở trang bằng HTTPS.' : 'GPS requires HTTPS or localhost. Open this page over HTTPS.');
      return;
    }
    if (!navigator.geolocation) { setGpsState('error'); setGpsMessage(t('gpsError')); return; }
    if (gpsWatchRef.current !== null) navigator.geolocation.clearWatch(gpsWatchRef.current);
    if (gpsTimeoutRef.current) clearTimeout(gpsTimeoutRef.current);

    let bestPoint: Coordinates | null = null;
    let lastAcceptedAccuracy = Infinity;
    let lastGeoErrorCode: number | undefined;
    let settled = false;
    const applyGpsPoint = (point: Coordinates) => {
      setLocation(point);
      setLocationSource('gps');
      setGpsState('ready');
      const accuracy = Math.round(point.accuracy ?? 0);
      setGpsMessage(language === 'vi'
        ? accuracy > 100 ? `GPS hiện sai số khoảng ${accuracy} m. Hãy bật định vị chính xác và thử ở nơi thoáng hơn.` : `Độ chính xác GPS khoảng ${accuracy} m.`
        : accuracy > 100 ? `GPS accuracy is about ${accuracy} m. Enable precise location and try in a more open area.` : `GPS accuracy is about ${accuracy} m.`);
      setWalkingRoute(null);
    };
    const finishInitialFix = (point: Coordinates | null, errorCode?: number) => {
      if (settled) return;
      settled = true;
      if (gpsTimeoutRef.current) clearTimeout(gpsTimeoutRef.current);
      gpsTimeoutRef.current = null;

      if (!point) {
        const denied = errorCode === 1;
        setGpsState(denied ? 'denied' : 'error');
        setGpsMessage(denied ? t('denied') : errorCode === 2
          ? language === 'vi' ? 'Thiết bị chưa xác định được vị trí. Bật Dịch vụ vị trí/GPS và thử ở nơi thoáng.' : 'The device could not determine your location. Enable Location Services/GPS and try outdoors.'
          : errorCode === 3
            ? language === 'vi' ? 'Thiết bị chưa trả vị trí sau 30 giây. Thử bật GPS chính xác hoặc kiểm tra quyền trình duyệt.' : 'No location fix after 30 seconds. Enable precise GPS or check browser permissions.'
            : t('gpsError'));
        return;
      }

      lastAcceptedAccuracy = point.accuracy ?? Infinity;
      applyGpsPoint(point);
      setSelectedPoiId(null); setMapPicker(false);
    };

    gpsWatchRef.current = navigator.geolocation.watchPosition((position) => {
      const point = { latitude: position.coords.latitude, longitude: position.coords.longitude, accuracy: position.coords.accuracy };
      const accuracy = point.accuracy ?? Infinity;
      if (!settled) {
        if (!bestPoint || accuracy < (bestPoint.accuracy ?? Infinity)) bestPoint = point;
        if ((bestPoint.accuracy ?? Infinity) <= 35) finishInitialFix(bestPoint);
        return;
      }
      if (accuracy <= Math.max(100, lastAcceptedAccuracy * 1.5)) {
        lastAcceptedAccuracy = accuracy;
        applyGpsPoint(point);
      }
    }, (errorValue) => {
      lastGeoErrorCode = errorValue.code;
      if (errorValue.code === errorValue.PERMISSION_DENIED) {
        finishInitialFix(bestPoint, errorValue.code);
        if (settled) {
          if (gpsWatchRef.current !== null) navigator.geolocation.clearWatch(gpsWatchRef.current);
          gpsWatchRef.current = null;
          setGpsMessage(t('denied'));
        }
      } else if (!settled && bestPoint) finishInitialFix(bestPoint, errorValue.code);
    }, { enableHighAccuracy: true, timeout: 30_000, maximumAge: 0 });
    gpsTimeoutRef.current = setTimeout(() => finishInitialFix(bestPoint, lastGeoErrorCode ?? 3), 30_000);
  }

  function chooseMapPoint(point: Coordinates) {
    if (gpsWatchRef.current !== null) navigator.geolocation.clearWatch(gpsWatchRef.current);
    gpsWatchRef.current = null;
    if (gpsTimeoutRef.current) clearTimeout(gpsTimeoutRef.current);
    gpsTimeoutRef.current = null;
    setLocation(point); setLocationSource('map'); setSelectedPoiId(null); setGpsMessage(''); setGpsState('ready'); setMapPicker(false); setWalkingRoute(null);
  }

  function selectPoi(id: number) {
    setAudioError(''); setCurrentTime(0); setIsPlaying(false); setSelectedPoiId(id);
    if (audioRef.current) { audioRef.current.pause(); audioRef.current.currentTime = 0; }
  }

  function toggleRoutePoi(id: number) {
    setRoutePoiIds((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]);
    setWalkingRoute(null);
    setRouteError('');
  }

  function moveRoutePoi(index: number, direction: -1 | 1) {
    setRoutePoiIds((current) => {
      const nextIndex = index + direction;
      if (nextIndex < 0 || nextIndex >= current.length) return current;
      const reordered = [...current];
      [reordered[index], reordered[nextIndex]] = [reordered[nextIndex], reordered[index]];
      return reordered;
    });
    setWalkingRoute(null);
    setRouteError('');
  }

  async function buildRoute() {
    if (routePois.length < 2) {
      setRouteError(language === 'vi' ? 'Chọn ít nhất 2 POI để tạo lộ trình.' : 'Choose at least 2 places to create a route.');
      return;
    }
    setRouteLoading(true);
    setRouteError('');
    try {
      setWalkingRoute(await createWalkingRoute(location ?? demoCenter, routePois));
    } catch (errorValue) {
      setWalkingRoute(null);
      setRouteError(errorValue instanceof Error ? errorValue.message : 'Không lấy được tuyến đi bộ.');
    } finally {
      setRouteLoading(false);
    }
  }

  function toggleRouteMode() {
    setRouteMode((current) => !current);
    setSelectedPoiId(null);
    setWalkingRoute(null);
    setRouteError('');
  }

  async function toggleAudio() {
    const audio = audioRef.current;
    if (!audio) return;
    setAudioError('');
    if (!selectedContent?.audioUrl) {
      setAudioError(narrationLanguage === 'vi' ? 'Audio chưa được tạo cho ngôn ngữ này; bạn vẫn có thể đọc nội dung bên dưới.' : 'Audio has not been generated for this language yet; you can still read the transcript below.');
      return;
    }
    try { if (audio.paused) await audio.play(); else audio.pause(); setIsPlaying(!audio.paused); }
    catch { setAudioError(language === 'vi' ? 'Không phát được audio. Hãy chạm phát lại.' : 'Audio could not play. Tap to try again.'); }
  }

  function skip(seconds: number) {
    if (audioRef.current && duration) audioRef.current.currentTime = Math.min(duration, Math.max(0, currentTime + seconds));
    else setCurrentTime(Math.max(0, currentTime + seconds));
  }

  function stopAudio() { setCurrentTime(0); setIsPlaying(false); if (audioRef.current) { audioRef.current.pause(); audioRef.current.currentTime = 0; } }
  function changeLanguage(next: Language) { setLanguage(next); setNarrationLanguage(next); void i18n.changeLanguage(next); stopAudio(); }
  function changeNarrationLanguage(next: Language) { setNarrationLanguage(next); setAudioError(''); stopAudio(); }
  const timeText = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

  return (
    <main className="app-shell">
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
            <MapContainer center={[location?.latitude ?? demoCenter.latitude, location?.longitude ?? demoCenter.longitude]} zoom={15} scrollWheelZoom className="leaflet-map" key={location ? `${location.latitude.toFixed(4)}-${location.longitude.toFixed(4)}` : 'initial-map'}>
              <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap contributors</a>' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
              <MapClickHandler onChoose={chooseMapPoint} />
              {walkingRoute && <><Polyline positions={walkingRoute.points} pathOptions={{ color: '#2563eb', weight: 5, opacity: 0.86, lineCap: 'round', lineJoin: 'round' }} /><FitRoute points={walkingRoute.points} /></>}
              {location && <Marker position={[location.latitude, location.longitude]} icon={locationIcon} />}
              {mapPois.map((poi, index) => {
                const stopNumber = routePoiIds.indexOf(poi.id);
                return <Marker key={poi.id} position={[poi.latitude, poi.longitude]} icon={poiIcon(routeMode ? (stopNumber >= 0 ? stopNumber + 1 : '·') : index + 1, routeMode ? stopNumber >= 0 : selectedPoiId === poi.id)} eventHandlers={{ click: () => routeMode ? toggleRoutePoi(poi.id) : selectPoi(poi.id) }} />;
              })}
            </MapContainer>
            <div className="map-topline"><div className="map-location"><span className={`status-dot ${locationSource === 'gps' ? 'status-live' : ''}`} />{locationSource === 'gps' ? t('gps') : locationSource === 'demo' ? t('demo') : locationSource === 'map' ? t('selected') : 'QUẬN 1 · TP. HỒ CHÍ MINH'}</div><button className="map-locate" onClick={locateMe} aria-label={t('locate')} title={t('locate')}><Icon name="compass" size={19} /></button></div>
            {mapPicker && <div className="map-instruction"><span className="instruction-icon"><Icon name="pin" size={17} /></span>{language === 'vi' ? 'Chạm vào bản đồ để chọn vị trí' : 'Tap the map to choose a location'}<button onClick={() => setMapPicker(false)} aria-label="Close"><Icon name="close" size={17} /></button></div>}
            {location && <div className="radius-label"><span />500 m radius</div>}
            {!location && <div className="map-empty-card"><span className="empty-pin"><Icon name="pin" size={20} /></span><b>{language === 'vi' ? 'Bắt đầu từ vị trí của bạn' : 'Start from your location'}</b><span>{language === 'vi' ? 'Bật GPS hoặc chọn một điểm trên bản đồ.' : 'Turn on GPS or choose a point on the map.'}</span></div>}
            <div className="map-credit">© OpenStreetMap contributors</div>
          </div>

          <div className="places-panel">
            <div className="places-heading"><div><div className="eyebrow places-eyebrow"><span className="eyebrow-line" /> {routeMode ? (language === 'vi' ? 'ĐI BỘ QUA CÁC ĐIỂM' : 'WALKING ITINERARY') : 'YOUR NEIGHBORHOOD'}</div><h2>{routeMode ? (language === 'vi' ? 'Lộ trình' : 'Your route') : selectedPoi ? t('listen') : t('near')}<span className="title-period">.</span></h2></div><span className="place-count">{routeMode ? String(routePoiIds.length).padStart(2, '0') : location ? String(nearby.length).padStart(2, '0') : '08'}</span></div>
            <div className="places-actions"><button className="primary-action" onClick={locateMe} disabled={gpsState === 'loading'}><Icon name="compass" size={18} />{gpsState === 'loading' ? t('loading') : t('locate')}</button><button className={`secondary-action ${mapPicker ? 'selected-action' : ''}`} onClick={() => setMapPicker(!mapPicker)}><Icon name="pin" size={17} />{t('choose')}</button><button className={`route-action ${routeMode ? 'route-action-active' : ''}`} onClick={toggleRouteMode} aria-pressed={routeMode}><Icon name="route" size={17} />{routeMode ? (language === 'vi' ? 'Đang chọn điểm' : 'Selecting stops') : (language === 'vi' ? 'Tạo lộ trình' : 'Plan route')}</button></div>
            {gpsMessage && <div className={`notice ${gpsState === 'ready' ? 'notice-soft' : ''}`} role="status"><span>{gpsState === 'ready' ? 'i' : '!'}</span><p>{gpsMessage}</p><button onClick={() => setGpsMessage('')} aria-label="Dismiss"><Icon name="close" size={16} /></button></div>}
            {routeMode ? (
              <div className="route-builder">
                <div className="route-origin"><span className="route-origin-dot" /><span>{language === 'vi' ? `Bắt đầu: ${locationSource === 'gps' ? 'vị trí GPS của bạn' : locationSource === 'map' ? 'vị trí đã chọn trên bản đồ' : 'Nhà hát Thành phố (demo)'}` : `Start: ${locationSource === 'gps' ? 'your GPS location' : locationSource === 'map' ? 'selected map point' : 'City Theatre (demo)'}`}</span></div>
                <p className="route-hint">{language === 'vi' ? 'Chọn từ 2 điểm trở lên. Thứ tự chạm là thứ tự ghé; dùng mũi tên để đổi.' : 'Choose at least 2 places. Selection order sets the visit order; use arrows to reorder.'}</p>
                <div className="route-stop-list" aria-label={language === 'vi' ? 'Các điểm trong lộ trình' : 'Route stops'}>
                  {routePois.map((poi, index) => <div className="route-stop" key={poi.id}><span className="route-stop-number">{String(index + 1).padStart(2, '0')}</span><span className="route-stop-name">{poi.name}</span><button onClick={() => moveRoutePoi(index, -1)} disabled={index === 0} aria-label={language === 'vi' ? `Đưa ${poi.name} lên trước` : `Move ${poi.name} earlier`}>↑</button><button onClick={() => moveRoutePoi(index, 1)} disabled={index === routePois.length - 1} aria-label={language === 'vi' ? `Đưa ${poi.name} xuống sau` : `Move ${poi.name} later`}>↓</button><button onClick={() => toggleRoutePoi(poi.id)} aria-label={language === 'vi' ? `Bỏ ${poi.name} khỏi lộ trình` : `Remove ${poi.name} from route`}><Icon name="close" size={15} /></button></div>)}
                </div>
                <div className="route-poi-picker"><div className="route-picker-title">{language === 'vi' ? 'CHỌN ĐIỂM THAM QUAN' : 'CHOOSE PLACES'}</div>{pois.map((poi) => { const included = routePoiIds.includes(poi.id); return <button type="button" key={poi.id} className={`route-choice ${included ? 'route-choice-selected' : ''}`} onClick={() => toggleRoutePoi(poi.id)} aria-pressed={included}><span className="route-check">{included ? '✓' : '+'}</span><span>{poi.name}</span></button>; })}</div>
                {walkingRoute && <div className="route-result" role="status"><b>{language === 'vi' ? 'Đã tạo tuyến đi bộ' : 'Walking route ready'}</b><span>{formatDistance(walkingRoute.distanceMeters, language === 'en')} · {Math.max(1, Math.round(walkingRoute.durationSeconds / 60))} {language === 'vi' ? 'phút đi bộ' : 'min walk'}</span></div>}
                {routeError && <div className="route-error" role="alert">{routeError}</div>}
                <button className="route-submit" onClick={() => void buildRoute()} disabled={routeLoading || routePoiIds.length < 2}><Icon name="route" size={17} />{routeLoading ? (language === 'vi' ? 'Đang tính đường…' : 'Finding route…') : walkingRoute ? (language === 'vi' ? 'Cập nhật lộ trình' : 'Update route') : (language === 'vi' ? `Tạo lộ trình (${routePoiIds.length})` : `Create route (${routePoiIds.length})`)}</button>
                {!walkingRoute && !routeError && routePoiIds.length < 2 && <small className="route-validation">{language === 'vi' ? 'Chọn thêm ít nhất một điểm nữa.' : 'Choose at least one more place.'}</small>}
              </div>
              ) : !location ? <div className="list-message list-message-empty"><span className="empty-number">01—08</span><p>{language === 'vi' ? 'Tám địa điểm đang chờ bạn khám phá quanh trung tâm Sài Gòn.' : 'Eight places are waiting for you around central Saigon.'}</p></div> : selectedPoi ? (
              <div className="detail-card" ref={selectedPoiRef}>
                <button className="back-link" onClick={() => { setSelectedPoiId(null); setAudioError(''); setIsPlaying(false); audioRef.current?.pause(); }}><Icon name="back" size={17} />{t('back')}</button>
                <div className="detail-image" style={{ backgroundImage: `linear-gradient(180deg, transparent 30%, rgba(24, 34, 30, .64)), url("${selectedPoi.thumbnailUrl ?? ''}")` }}><span className="image-category">{selectedPoi.category ?? 'Điểm tham quan'}</span><div className="image-title">{selectedPoi.name}</div></div>
                <div className="detail-meta"><span><Icon name="pin" size={14} />{language === 'vi' ? 'Điểm tham quan' : 'Place to explore'}</span><label>{language === 'vi' ? 'NGÔN NGỮ THUYẾT MINH' : 'NARRATION LANGUAGE'}<select value={narrationLanguage} onChange={(event) => changeNarrationLanguage(event.target.value as Language)}>{narrationLanguages.map((item) => <option key={item.code} value={item.code}>{item.label}</option>)}</select></label></div>
                <audio ref={audioRef} src={selectedContent?.audioUrl ?? undefined} preload="none" onError={() => { if (selectedContent?.audioUrl) setAudioError(narrationLanguage === 'vi' ? 'Không tải được audio. Vui lòng thử lại.' : 'Could not load audio. Please try again.'); }} />
                <div className="audio-player"><input aria-label={language === 'vi' ? 'Tiến độ audio' : 'Audio progress'} type="range" min={0} max={duration || 100} value={duration ? currentTime : 0} onChange={(event) => { if (audioRef.current && duration) audioRef.current.currentTime = Number(event.target.value); }} style={{ '--progress': `${duration ? (currentTime / duration) * 100 : 0}%` } as React.CSSProperties} /><div className="audio-times"><span>{timeText(currentTime)}</span><span>{duration ? timeText(duration) : '—:—'}</span></div><div className="audio-controls"><button className="skip-button" onClick={() => skip(-10)} aria-label="Back 10 seconds"><span>−10</span></button><button className="play-button" onClick={toggleAudio} aria-label={isPlaying ? 'Pause' : 'Play'}><Icon name={isPlaying ? 'pause' : 'play'} size={22} /></button><button className="skip-button" onClick={() => skip(10)} aria-label="Forward 10 seconds"><span>+10</span></button><button className="speed-button" onClick={() => { if (audioRef.current) audioRef.current.playbackRate = audioRef.current.playbackRate >= 1.5 ? 0.75 : audioRef.current.playbackRate + 0.25; }}>{audioRef.current?.playbackRate ?? 1}×</button></div>{audioError && <div className="audio-note" role="status">{audioError}</div>}</div>
                <div className="transcript"><div className="transcript-heading"><span className="transcript-icon"><Icon name="volume" size={15} /></span><span>{t('read')}</span><i /></div><p>{selectedContent?.text ?? selectedPoi.descriptions?.[narrationLanguage] ?? selectedPoi.originalDescription ?? ''}</p>{selectedContent && !selectedContent.audioUrl && <small className="audio-note">{language === 'vi' ? 'Audio mẫu chưa có sẵn cho ngôn ngữ này.' : 'Sample audio is not available for this language.'}</small>}</div>
              </div>
            ) : (
              <>
                <div className="nearby-summary"><span className="summary-icon"><Icon name="pin" size={15} /></span><span>{nearby.length} {language === 'vi' ? 'địa điểm' : 'places'}</span><span className="summary-dot">·</span><span>{language === 'vi' ? 'Điểm tham quan tại Quận 1' : 'Places in District 1'}</span><button className="route-inline-link" onClick={toggleRouteMode}>{language === 'vi' ? 'Chọn nhiều điểm →' : 'Select multiple →'}</button></div>
                <div className="poi-list">{nearby.map((poi, index) => <button className="poi-row" key={poi.id} onClick={() => selectPoi(poi.id)}><span className="poi-index">{String(index + 1).padStart(2, '0')}</span><span className="poi-row-body"><span className="poi-name">{poi.name}</span><span className="poi-category">{poi.category}</span></span><span className="poi-arrow"><Icon name="arrow" size={16} /></span></button>)}</div>
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
