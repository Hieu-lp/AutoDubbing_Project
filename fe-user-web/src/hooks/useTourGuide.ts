import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { Coordinates, Poi, PoiContent, ProximitySettings, VisitorLocation } from '../types';
import { isReady, preparationKey } from '../services/api';
import type { PreparationClient } from '../services/preparationClient';
import { distanceMeters, gpsPolicy, ProximityTracker, trustedLocation } from '../tour/proximity';
import { tourDiagnostic } from '../tour/diagnostics';

interface GuideInput {
  pois: Poi[];
  settings?: ProximitySettings;
  preparation: PreparationClient;
  selectedId: number | null;
  language: string;
  routeMode: boolean;
  getCached: (id: number, language: string) => PoiContent | undefined;
  readSource: (poi: Poi, signal: AbortSignal) => Promise<PoiContent>;
  onLocation: (point: VisitorLocation | null) => void;
  onGpsState: (state: 'loading' | 'ready' | 'denied' | 'error', message: string) => void;
  onAutoSelect: (poi: Poi) => void;
  player: { play: (content: PoiContent, valid: () => boolean, kind?: 'auto' | 'manual') => Promise<boolean>; stop: () => void;
    cancelPending: () => void; busy: () => boolean };
}

export function useTourGuide(input: GuideInput) {
  const latest = useRef(input);
  useLayoutEffect(() => { latest.current = input; });
  const [running, setRunning] = useState(false);
  const [reliable, setReliable] = useState(false);
  const active = useRef(false);
  const watch = useRef<number | null>(null);
  const interval = useRef<ReturnType<typeof setInterval> | null>(null);
  const fix = useRef<VisitorLocation | null>(null);
  const source = useRef<'gps' | 'map'>('gps');
  const tracker = useRef(new ProximityTracker());
  const heard = useRef(new Set<number>());
  const attempted = useRef(new Set<number>());
  const reads = useRef(new Set<number>());
  const controllers = useRef(new Map<number, AbortController>());
  const epoch = useRef(0);
  const choice = useRef(0);
  const locating = useRef(0);
  const playing = useRef<{ id: number; valid: () => boolean } | null>(null);
  const alive = useRef(true);
  const pendingMapStart = useRef(false);
  const lastDecision = useRef('');
  const cancelReads = useCallback(() => {
    for (const controller of controllers.current.values()) controller.abort();
    controllers.current.clear(); reads.current.clear();
  }, []);
  const invalidate = useCallback((reason = 'selection-changed') => {
    choice.current++; tracker.current.reset(reason); cancelReads();
    if (playing.current) attempted.current.delete(playing.current.id);
    latest.current.player.cancelPending(); playing.current = null;
  }, [cancelReads]);
  const notePlayed = useCallback((id: number) => { heard.current.add(id); }, []);
  const cleanup = useCallback(() => {
    active.current = false; epoch.current++; locating.current++;
    if (watch.current !== null) navigator.geolocation?.clearWatch(watch.current);
    if (interval.current !== null) clearInterval(interval.current);
    watch.current = null; interval.current = null; invalidate('session-cleanup');
    latest.current.preparation.stopNearby();
  }, [invalidate]);
  const stop = useCallback(() => {
    pendingMapStart.current = false; tourDiagnostic('session-stop');
    cleanup(); latest.current.preparation.cancelAll(); latest.current.player.stop();
    setRunning(false); setReliable(false);
  }, [cleanup]);
  const receive = useCallback((position: GeolocationPosition) => {
    if (!alive.current) return;
    const point: VisitorLocation = { source: 'gps', latitude: position.coords.latitude, longitude: position.coords.longitude,
      accuracy: position.coords.accuracy, timestamp: position.timestamp };
    fix.current = point; latest.current.onLocation(point);
    tourDiagnostic('location', { ...point, active: active.current });
    const reliable = trustedLocation(point);
    setReliable(reliable);
    latest.current.onGpsState('ready', reliable ? `Độ chính xác GPS khoảng ${Math.round(position.coords.accuracy)} m.`
      : `GPS chưa đủ tin cậy để tự phát (cần sai số ≤ ${gpsPolicy.maxAccuracyMeters} m và vị trí mới).`);
    if (active.current && latest.current.settings) tracker.current.update(latest.current.pois,
      document.hidden ? null : point, latest.current.settings, Date.now(), performance.now());
    if (!reliable) tracker.current.reset();
  }, []);
  const requestGps = useCallback(() => {
    if (!window.isSecureContext || !navigator.geolocation) {
      latest.current.onGpsState('error', 'GPS cần HTTPS hoặc localhost và quyền truy cập vị trí.'); return;
    }
    const request = ++locating.current;
    latest.current.onGpsState('loading', 'Đang tìm vị trí…');
    const success = (position: GeolocationPosition) => {
      if (request === locating.current && alive.current && source.current === 'gps') receive(position);
    };
    const failure = (failure: GeolocationPositionError) => {
      if (request !== locating.current || !alive.current || source.current !== 'gps') return;
      fix.current = null; invalidate('gps-unavailable'); setReliable(false); latest.current.preparation.stopNearby();
      latest.current.onGpsState(failure.code === 1 ? 'denied' : 'error', failure.code === 1
        ? 'Bạn chưa cấp quyền vị trí. Hãy chọn vị trí trên bản đồ hoặc nghe thủ công.'
        : 'Chưa lấy được vị trí. Bạn có thể chọn vị trí trên bản đồ.');
      if (failure.code === 1 && active.current) { cleanup(); setRunning(false); }
    };
    const options = { enableHighAccuracy: true, timeout: 15_000, maximumAge: 0 };
    if (active.current) watch.current = navigator.geolocation.watchPosition(success, failure, options);
    else navigator.geolocation.getCurrentPosition(success, failure, options);
  }, [cleanup, invalidate, receive]);
  const locate = useCallback(() => {
    pendingMapStart.current = false;
    locating.current++;
    if (watch.current !== null) navigator.geolocation?.clearWatch(watch.current);
    watch.current = null; source.current = 'gps'; fix.current = null;
    invalidate('gps-selected'); latest.current.player.stop(); latest.current.preparation.stopNearby(true);
    tourDiagnostic('location', { source: 'gps', waitingForNewFix: true, active: active.current });
    setReliable(false); latest.current.onLocation(null); requestGps();
  }, [invalidate, requestGps]);
  const tick = useCallback(() => {
    const current = latest.current;
    if (!active.current || !current.settings) return;
    const snapshot = () => tracker.current.update(latest.current.pois, document.hidden ? null : fix.current,
      latest.current.settings!, Date.now(), performance.now());
    const state = snapshot();
    const fresh = !document.hidden && trustedLocation(fix.current);
    setReliable(fresh);
    if (playing.current && !playing.current.valid()) {
      attempted.current.delete(playing.current.id);
      current.player.cancelPending(); playing.current = null;
    }
    current.preparation.setInterest('nearby', state.nearby.map(poi => preparationKey(poi.id, poi.originalLanguage)));
    if (!fresh) {
      cancelReads(); current.preparation.stopNearby(); return;
    }
    for (const [id, controller] of controllers.current) if (!state.eligible.some(poi => poi.id === id)) {
      controller.abort(); controllers.current.delete(id); reads.current.delete(id);
    }
    void current.preparation.prepareNearby(fix.current!, state.nearby.map(poi => poi.id));
    const candidate = state.eligible[0];
    const allowed = (poi: Poi) => !latest.current.routeMode && (latest.current.selectedId === null
      || latest.current.selectedId === poi.id && latest.current.language === poi.originalLanguage);
    const blocked = !candidate ? 'waiting-for-dwell' : !allowed(candidate) ? 'selection-or-route'
      : heard.current.has(candidate.id) ? 'already-heard' : attempted.current.has(candidate.id) ? 'already-attempted'
      : playing.current || current.player.busy() ? 'player-busy' : '';
    const decision = `${candidate?.id}:${blocked}`;
    if (decision !== lastDecision.current) {
      lastDecision.current = decision;
      tourDiagnostic('playback-decision', { source: source.current, active: active.current, reason: blocked || 'eligible',
        nearby: state.nearby.map(poi => ({ id: poi.id, distance: distanceMeters(fix.current!, poi) })),
        eligible: state.eligible.map(poi => poi.id), heard: [...heard.current], attempted: [...attempted.current] });
    }
    if (!candidate || blocked) return;
    const content = current.getCached(candidate.id, candidate.originalLanguage);
    if (!isReady(content, candidate.originalLanguage)) {
      if (!reads.current.has(candidate.id)) {
        reads.current.add(candidate.id);
        const controller = new AbortController(); controllers.current.set(candidate.id, controller);
        tourDiagnostic('source-read', { poiId: candidate.id, cachedReady: false });
        void current.readSource(candidate, controller.signal).then(saved => {
          if (!controller.signal.aborted && !isReady(saved, candidate.originalLanguage))
            return latest.current.preparation.prepareLanguage(candidate.id, candidate.originalLanguage);
        }).catch(() => {
          if (!controller.signal.aborted) latest.current.onGpsState('ready', 'Nội dung hiện chưa sẵn sàng. Bạn vẫn có thể chọn điểm để đọc.');
        }).finally(() => { if (controllers.current.get(candidate.id) === controller) controllers.current.delete(candidate.id); });
      }
      return;
    }
    const session = epoch.current; const selection = choice.current;
    const valid = () => active.current && session === epoch.current && selection === choice.current
      && !document.hidden && trustedLocation(fix.current) && snapshot().eligible[0]?.id === candidate.id
      && allowed(candidate) && !heard.current.has(candidate.id);
    if (!valid()) return;
    const attempt = { id: candidate.id, valid }; playing.current = attempt;
    attempted.current.add(candidate.id);
    current.onAutoSelect(candidate);
    void current.player.play(content, valid, 'auto').then(success => {
      if (success) heard.current.add(candidate.id);
      tourDiagnostic('autoplay-result', { poiId: candidate.id, success, heard: [...heard.current] });
    })
      .finally(() => { if (playing.current === attempt) playing.current = null; });
  }, [cancelReads]);
  const start = useCallback(() => {
    if (!latest.current.settings) {
      pendingMapStart.current = source.current === 'map';
      latest.current.onGpsState('loading', 'Đang tải cấu hình để bắt đầu thuyết minh tại vị trí đã chọn.'); return;
    }
    pendingMapStart.current = false;
    cleanup(); latest.current.player.stop(); latest.current.preparation.resetVisit();
    heard.current.clear(); attempted.current.clear(); reads.current.clear();
    active.current = true; setRunning(true); setReliable(trustedLocation(fix.current));
    tourDiagnostic('session-start', { source: source.current, epoch: epoch.current });
    if (source.current === 'map') latest.current.onGpsState('ready', 'Đang dùng vị trí đã chọn trên bản đồ làm vị trí du khách.');
    else {
      fix.current = null; setReliable(false); requestGps();
    }
    interval.current = setInterval(tick, 250);
    tick();
  }, [cleanup, requestGps, tick]);
  const chooseMapPoint = useCallback((point: Coordinates) => {
    const chosen: VisitorLocation = { latitude: point.latitude, longitude: point.longitude, source: 'map' };
    if (!trustedLocation(chosen)) return;
    locating.current++;
    if (watch.current !== null) navigator.geolocation?.clearWatch(watch.current);
    watch.current = null; source.current = 'map'; fix.current = chosen;
    invalidate('map-location-changed'); latest.current.player.stop(); latest.current.preparation.stopNearby(true);
    setReliable(true); latest.current.onLocation(chosen);
    latest.current.onGpsState('ready', 'Đang dùng vị trí đã chọn trên bản đồ làm vị trí du khách.');
    tourDiagnostic('location', { ...chosen, active: active.current });
    if (!active.current) start();
    else tick();
  }, [invalidate, start, tick]);
  useEffect(() => {
    if (pendingMapStart.current && input.settings && source.current === 'map') start();
  }, [input.settings, start]);
  useEffect(() => {
    alive.current = true;
    const visibility = () => {
      invalidate(document.hidden ? 'page-hidden' : 'page-visible');
      if (document.hidden) latest.current.preparation.stopNearby();
    };
    document.addEventListener('visibilitychange', visibility);
    return () => { alive.current = false; cleanup(); document.removeEventListener('visibilitychange', visibility); };
  }, [cleanup, invalidate]);
  return { running, reliable, start, stop, locate, chooseMapPoint, invalidate, notePlayed };
}
