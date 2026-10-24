import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Circle, MapContainer, Marker, TileLayer, useMapEvents } from 'react-leaflet';
import L from 'leaflet';
import type { Coordinates, Language, NearbyPoi } from './types';
import { getPois } from './services/poiService';

const demoCenter: Coordinates = { latitude: 10.7769, longitude: 106.7009 };
const scanRadius = 500;
const locationIcon = L.divIcon({ className: 'location-marker', html: '<span></span>', iconSize: [24, 24], iconAnchor: [12, 12] });
const poiIcon = (number: number, selected: boolean) => L.divIcon({ className: 'poi-marker-wrap', html: `<span class="poi-marker ${selected ? 'poi-marker-selected' : ''}">${number}</span>`, iconSize: [34, 42], iconAnchor: [17, 38] });

function distanceMeters(a: Coordinates, b: Coordinates) {
  const rad = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * rad;
  const dLng = (b.longitude - a.longitude) * rad;
  const value = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLng / 2) ** 2;
  return 6_371_000 * 2 * Math.atan2(Math.sqrt(value), Math.sqrt(1 - value));
}

function formatDistance(distance: number, english: boolean) {
  return distance < 1000 ? `${Math.round(distance)} m` : `${(distance / 1000).toFixed(1)} ${english ? 'km' : 'km'}`;
}

function MapClickHandler({ onChoose }: { onChoose: (point: Coordinates) => void }) {
  useMapEvents({ click(event) { onChoose({ latitude: event.latlng.lat, longitude: event.latlng.lng }); } });
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
  return <svg {...common}><circle cx="12" cy="12" r="9"/><path d="M12 8v4l3 2"/></svg>;
}

export default function App() {
  const { t, i18n } = useTranslation();
  const { data: pois = [], isLoading, isError, error } = useQuery({ queryKey: ['pois'], queryFn: getPois });
  const [location, setLocation] = useState<Coordinates | null>(null);
  const [locationSource, setLocationSource] = useState<'gps' | 'map' | 'demo' | null>(null);
  const [gpsState, setGpsState] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  const [gpsMessage, setGpsMessage] = useState('');
  const [selectedPoiId, setSelectedPoiId] = useState<number | null>(null);
  const [language, setLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'vi');
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [audioError, setAudioError] = useState('');
  const [mapPicker, setMapPicker] = useState(false);
  const audioRef = useRef<HTMLAudioElement>(null);
  const selectedPoiRef = useRef<HTMLDivElement>(null);

  const nearby = useMemo<NearbyPoi[]>(() => {
    if (!location) return [];
    return pois.map((poi) => ({ ...poi, distance: distanceMeters(location, poi) })).filter((poi) => poi.distance <= scanRadius).sort((a, b) => a.distance - b.distance);
  }, [location, pois]);
  const selectedPoi = pois.find((poi) => poi.id === selectedPoiId) ?? null;
  const selectedNearby = nearby.find((poi) => poi.id === selectedPoiId);

  useEffect(() => { if (selectedPoiId) selectedPoiRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, [selectedPoiId]);
  useEffect(() => {
    const player = audioRef.current;
    if (!player) return;
    const update = () => setCurrentTime(player.currentTime || 0);
    const loaded = () => { setDuration(player.duration || 0); setAudioError(''); };
    const ended = () => setIsPlaying(false);
    player.addEventListener('timeupdate', update); player.addEventListener('loadedmetadata', loaded); player.addEventListener('ended', ended);
    return () => { player.removeEventListener('timeupdate', update); player.removeEventListener('loadedmetadata', loaded); player.removeEventListener('ended', ended); };
  }, [selectedPoiId, language]);

  function locateMe() {
    setGpsMessage(''); setGpsState('loading');
    if (!navigator.geolocation) { setGpsState('error'); setGpsMessage(t('gpsError')); return; }
    navigator.geolocation.getCurrentPosition((position) => {
      const point = { latitude: position.coords.latitude, longitude: position.coords.longitude, accuracy: position.coords.accuracy };
      const insideDemoArea = point.latitude >= 10.765 && point.latitude <= 10.795 && point.longitude >= 106.685 && point.longitude <= 106.715;
      if (!insideDemoArea) { setLocation(demoCenter); setLocationSource('demo'); setGpsState('ready'); setGpsMessage(t('outside')); }
      else { setLocation(point); setLocationSource('gps'); setGpsState('ready'); }
      setSelectedPoiId(null); setMapPicker(false);
    }, (errorValue) => {
      const denied = errorValue.code === errorValue.PERMISSION_DENIED;
      setGpsState(denied ? 'denied' : 'error'); setGpsMessage(denied ? t('denied') : t('gpsError'));
    }, { enableHighAccuracy: true, timeout: 12_000, maximumAge: 10_000 });
  }

  function chooseMapPoint(point: Coordinates) {
    setLocation(point); setLocationSource('map'); setSelectedPoiId(null); setGpsMessage(''); setGpsState('ready'); setMapPicker(false);
  }

  function selectPoi(id: number) {
    setAudioError(''); setCurrentTime(0); setIsPlaying(false); setSelectedPoiId(id);
    if (audioRef.current) { audioRef.current.pause(); audioRef.current.currentTime = 0; }
  }

  async function toggleAudio() {
    const audio = audioRef.current;
    if (!audio) return;
    setAudioError('');
    if (!selectedPoi?.audioUrls[language]) {
      setAudioError(language === 'vi' ? 'Audio demo chưa được thêm; bạn có thể đọc bản thuyết minh bên dưới.' : 'Demo audio is not available yet; read the transcript below.');
      return;
    }
    try { if (audio.paused) await audio.play(); else audio.pause(); setIsPlaying(!audio.paused); }
    catch { setAudioError(language === 'vi' ? 'Không phát được audio. Hãy chạm phát lại.' : 'Audio could not play. Tap to try again.'); }
  }

  function skip(seconds: number) {
    if (audioRef.current && duration) audioRef.current.currentTime = Math.min(duration, Math.max(0, currentTime + seconds));
    else setCurrentTime(Math.max(0, currentTime + seconds));
  }

  function changeLanguage(next: Language) { setLanguage(next); void i18n.changeLanguage(next); setCurrentTime(0); setIsPlaying(false); if (audioRef.current) { audioRef.current.pause(); audioRef.current.currentTime = 0; } }
  const arrived = Boolean(nearby[0] && nearby[0].distance <= nearby[0].proximityRadius);
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
              {location && <><Circle center={[location.latitude, location.longitude]} radius={scanRadius} pathOptions={{ color: '#d67a4a', weight: 1.5, fillColor: '#e9a878', fillOpacity: 0.12, dashArray: '5 7' }} /><Marker position={[location.latitude, location.longitude]} icon={locationIcon} />{location.accuracy && <Circle center={[location.latitude, location.longitude]} radius={Math.min(location.accuracy, 150)} pathOptions={{ color: '#286e67', weight: 1, fillColor: '#4b9a89', fillOpacity: 0.08 }} />}</>}
              {nearby.map((poi, index) => <Marker key={poi.id} position={[poi.latitude, poi.longitude]} icon={poiIcon(index + 1, selectedPoiId === poi.id)} eventHandlers={{ click: () => selectPoi(poi.id) }} />)}
            </MapContainer>
            <div className="map-topline"><div className="map-location"><span className={`status-dot ${locationSource === 'gps' ? 'status-live' : ''}`} />{locationSource === 'gps' ? t('gps') : locationSource === 'demo' ? t('demo') : locationSource === 'map' ? t('selected') : 'QUẬN 1 · TP. HỒ CHÍ MINH'}</div><button className="map-locate" onClick={locateMe} aria-label={t('locate')} title={t('locate')}><Icon name="compass" size={19} /></button></div>
            {mapPicker && <div className="map-instruction"><span className="instruction-icon"><Icon name="pin" size={17} /></span>{language === 'vi' ? 'Chạm vào bản đồ để chọn vị trí' : 'Tap the map to choose a location'}<button onClick={() => setMapPicker(false)} aria-label="Close"><Icon name="close" size={17} /></button></div>}
            {location && <div className="radius-label"><span />500 m radius</div>}
            {!location && <div className="map-empty-card"><span className="empty-pin"><Icon name="pin" size={20} /></span><b>{language === 'vi' ? 'Bắt đầu từ vị trí của bạn' : 'Start from your location'}</b><span>{language === 'vi' ? 'Bật GPS hoặc chọn một điểm trên bản đồ.' : 'Turn on GPS or choose a point on the map.'}</span></div>}
            <div className="map-credit">© OpenStreetMap contributors</div>
          </div>

          <div className="places-panel">
            <div className="places-heading"><div><div className="eyebrow places-eyebrow"><span className="eyebrow-line" /> YOUR NEIGHBORHOOD</div><h2>{selectedPoi ? t('listen') : t('near')}<span className="title-period">.</span></h2></div><span className="place-count">{location ? String(nearby.length).padStart(2, '0') : '08'}</span></div>
            <div className="places-actions"><button className="primary-action" onClick={locateMe} disabled={gpsState === 'loading'}><Icon name="compass" size={18} />{gpsState === 'loading' ? t('loading') : t('locate')}</button><button className={`secondary-action ${mapPicker ? 'selected-action' : ''}`} onClick={() => setMapPicker(!mapPicker)}><Icon name="pin" size={17} />{t('choose')}</button></div>
            {gpsMessage && <div className={`notice ${gpsState === 'ready' ? 'notice-soft' : ''}`} role="status"><span>{gpsState === 'ready' ? 'i' : '!'}</span><p>{gpsMessage}</p><button onClick={() => setGpsMessage('')} aria-label="Dismiss"><Icon name="close" size={16} /></button></div>}
            {isLoading ? <div className="list-message">{language === 'vi' ? 'Đang tải địa điểm…' : 'Loading places…'}</div> : isError ? <div className="list-message error-message">{(error as Error).message}</div> : !location ? <div className="list-message list-message-empty"><span className="empty-number">01—08</span><p>{language === 'vi' ? 'Tám địa điểm đang chờ bạn khám phá quanh trung tâm Sài Gòn.' : 'Eight places are waiting for you around central Saigon.'}</p></div> : nearby.length === 0 ? <div className="list-message list-message-empty"><span className="empty-number">—</span><p>{t('noPois')}</p></div> : selectedPoi ? (
              <div className="detail-card" ref={selectedPoiRef}>
                <button className="back-link" onClick={() => { setSelectedPoiId(null); setAudioError(''); setIsPlaying(false); audioRef.current?.pause(); }}><Icon name="back" size={17} />{t('back')}</button>
                <div className="detail-image" style={{ backgroundImage: `linear-gradient(180deg, transparent 30%, rgba(24, 34, 30, .64)), url("${selectedPoi.thumbnailUrl ?? ''}")` }}><span className="image-category">{selectedPoi.category}</span><div className="image-title">{selectedPoi.name}</div></div>
                <div className="detail-meta"><span><Icon name="pin" size={14} />{formatDistance(selectedNearby?.distance ?? 0, language === 'en')} {language === 'vi' ? 'từ bạn' : 'away'}</span><label>{language === 'vi' ? 'BẢN ĐỌC' : 'NARRATION'}<select value={language} onChange={(event) => changeLanguage(event.target.value as Language)}><option value="vi">🇻🇳　Tiếng Việt</option><option value="en">🇬🇧　English</option></select></label></div>
                <audio ref={audioRef} src={selectedPoi.audioUrls[language]} preload="none" onError={() => { if (selectedPoi.audioUrls[language]) setAudioError(language === 'vi' ? 'Không tải được audio. Vui lòng thử lại.' : 'Could not load audio. Please try again.'); }} />
                <div className="audio-player"><input aria-label={language === 'vi' ? 'Tiến độ audio' : 'Audio progress'} type="range" min={0} max={duration || 100} value={duration ? currentTime : 0} onChange={(event) => { if (audioRef.current && duration) audioRef.current.currentTime = Number(event.target.value); }} style={{ '--progress': `${duration ? (currentTime / duration) * 100 : 0}%` } as React.CSSProperties} /><div className="audio-times"><span>{timeText(currentTime)}</span><span>{duration ? timeText(duration) : '—:—'}</span></div><div className="audio-controls"><button className="skip-button" onClick={() => skip(-10)} aria-label="Back 10 seconds"><span>−10</span></button><button className="play-button" onClick={toggleAudio} aria-label={isPlaying ? 'Pause' : 'Play'}><Icon name={isPlaying ? 'pause' : 'play'} size={22} /></button><button className="skip-button" onClick={() => skip(10)} aria-label="Forward 10 seconds"><span>+10</span></button><button className="speed-button" onClick={() => { if (audioRef.current) audioRef.current.playbackRate = audioRef.current.playbackRate >= 1.5 ? 0.75 : audioRef.current.playbackRate + 0.25; }}>{audioRef.current?.playbackRate ?? 1}×</button></div>{audioError && <div className="audio-note" role="status">{audioError}</div>}</div>
                <div className="transcript"><div className="transcript-heading"><span className="transcript-icon"><Icon name="volume" size={15} /></span><span>{t('read')}</span><i /></div><p>{selectedPoi.descriptions[language]}</p></div>
              </div>
            ) : (
              <>
                <div className="nearby-summary"><span className="summary-icon"><Icon name="pin" size={15} /></span><span>{nearby.length} {t('nearby')}</span><span className="summary-dot">·</span><span>{language === 'vi' ? 'Xếp theo khoảng cách' : 'Sorted by distance'}</span></div>
                <div className="poi-list">{nearby.map((poi, index) => <button className="poi-row" key={poi.id} onClick={() => selectPoi(poi.id)}><span className="poi-index">{String(index + 1).padStart(2, '0')}</span><span className="poi-row-body"><span className="poi-name">{poi.name}</span><span className="poi-category">{poi.category}</span></span><span className="poi-distance">{formatDistance(poi.distance, language === 'en')}</span><span className="poi-arrow"><Icon name="arrow" size={16} /></span></button>)}</div>
                <div className="arrive-tip"><span className={`arrive-icon ${arrived ? 'arrive-now' : ''}`}><Icon name="volume" size={16} /></span><p>{arrived ? t('arrived') : language === 'vi' ? 'Chạm vào một địa điểm để nghe câu chuyện.' : 'Tap a place to listen to its story.'}</p><span className="tip-wave"><i /><i /><i /><i /><i /></span></div>
              </>
            )}
          </div>
        </section>

        <section className="explore-footer"><div className="footer-star">✳</div><p>{language === 'vi' ? 'Đi chậm thôi. Thành phố có nhiều điều để kể.' : 'Take your time. The city has stories to tell.'}</p><span>HO CHI MINH CITY · 2026</span></section>
      </div>
    </main>
  );
}
