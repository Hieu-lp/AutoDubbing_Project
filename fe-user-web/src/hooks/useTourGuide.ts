import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { Coordinates, Poi, PoiContent, ProximitySettings } from '../types';
import { isReady, preparationKey } from '../services/api';
import type { PreparationClient } from '../services/preparationClient';
import { gpsPolicy, ProximityTracker, trustedFix } from '../tour/proximity';

interface GuideInput {
  pois: Poi[];
  settings?: ProximitySettings;
  preparation: PreparationClient;
  selectedId: number | null;
  language: string;
  routeMode: boolean;
  getCached: (id: number, language: string) => PoiContent | undefined;
  readSource: (poi: Poi, signal: AbortSignal) => Promise<PoiContent>;
  onLocation: (point: Coordinates) => void;
  onGpsState: (state: 'loading' | 'ready' | 'denied' | 'error', message: string) => void;
  onAutoSelect: (poi: Poi) => void;
  player: { play: (content: PoiContent, valid: () => boolean) => Promise<boolean>; stop: () => void; busy: () => boolean };
}

export function useTourGuide(input: GuideInput) {
  const latest = useRef(input);
  useLayoutEffect(() => { latest.current = input; });
  const [running, setRunning] = useState(false);
  const [reliable, setReliable] = useState(false);
  const active = useRef(false);
  const watch = useRef<number | null>(null);
  const interval = useRef<ReturnType<typeof setInterval> | null>(null);
  const fix = useRef<Coordinates | null>(null);
  const tracker = useRef(new ProximityTracker());
  const heard = useRef(new Set<number>());
  const attempted = useRef(new Set<number>());
  const reads = useRef(new Set<number>());
  const controllers = useRef(new Map<number, AbortController>());
  const epoch = useRef(0);
  const choice = useRef(0);
  const locating = useRef(0);
  const playing = useRef(false);
  const alive = useRef(true);
  const invalidate = useCallback(() => { choice.current++; tracker.current.reset(); }, []);
  const notePlayed = useCallback((id: number) => { heard.current.add(id); }, []);
  const cleanup = useCallback(() => {
    active.current = false; epoch.current++; locating.current++;
    if (watch.current !== null) navigator.geolocation?.clearWatch(watch.current);
    if (interval.current !== null) clearInterval(interval.current);
    watch.current = null; interval.current = null; fix.current = null; tracker.current.reset();
    for (const controller of controllers.current.values()) controller.abort();
    controllers.current.clear(); latest.current.preparation.stopNearby();
  }, []);
  const stop = useCallback(() => {
    cleanup(); latest.current.preparation.cancelAll(); latest.current.player.stop();
    setRunning(false); setReliable(false);
  }, [cleanup]);
  const receive = useCallback((position: GeolocationPosition) => {
    if (!alive.current) return;
    const point = { latitude: position.coords.latitude, longitude: position.coords.longitude,
      accuracy: position.coords.accuracy, timestamp: position.timestamp };
    fix.current = point; latest.current.onLocation(point);
    const reliable = trustedFix(point);
    setReliable(reliable);
    latest.current.onGpsState('ready', reliable ? `Độ chính xác GPS khoảng ${Math.round(point.accuracy)} m.`
      : `GPS chưa đủ tin cậy để tự phát (cần sai số ≤ ${gpsPolicy.maxAccuracyMeters} m và vị trí mới).`);
    if (!reliable) tracker.current.reset();
  }, []);
  const locate = useCallback(() => {
    if (!window.isSecureContext || !navigator.geolocation) {
      latest.current.onGpsState('error', 'GPS cần HTTPS hoặc localhost và quyền truy cập vị trí.'); return;
    }
    const request = ++locating.current;
    latest.current.onGpsState('loading', 'Đang tìm vị trí…');
    navigator.geolocation.getCurrentPosition(position => {
      if (request === locating.current && alive.current) receive(position);
    }, failure => {
      if (request !== locating.current || !alive.current) return;
      latest.current.onGpsState(failure.code === 1 ? 'denied' : 'error', failure.code === 1
        ? 'Bạn chưa cấp quyền vị trí. Bạn vẫn có thể chọn vị trí trên bản đồ và nghe thủ công.'
        : 'Chưa lấy được vị trí. Bạn có thể chọn vị trí trên bản đồ.');
    }, { enableHighAccuracy: true, timeout: 15_000, maximumAge: 0 });
  }, [receive]);
  const tick = useCallback(() => {
    const current = latest.current;
    if (!active.current || !current.settings) return;
    const snapshot = () => tracker.current.update(latest.current.pois, document.hidden ? null : fix.current,
      latest.current.settings!, Date.now(), performance.now());
    const state = snapshot();
    const fresh = !document.hidden && trustedFix(fix.current);
    setReliable(fresh);
    current.preparation.setInterest('nearby', state.nearby.map(poi => preparationKey(poi.id, poi.originalLanguage)));
    if (!fresh) {
      for (const [id, controller] of controllers.current) { controller.abort(); reads.current.delete(id); }
      controllers.current.clear(); current.preparation.stopNearby(); return;
    }
    void current.preparation.prepareNearby(fix.current!, state.nearby.map(poi => poi.id));
    const candidate = state.eligible[0];
    const allowed = (poi: Poi) => !latest.current.routeMode && (latest.current.selectedId === null
      || latest.current.selectedId === poi.id && latest.current.language === poi.originalLanguage);
    if (!candidate || !allowed(candidate) || heard.current.has(candidate.id) || attempted.current.has(candidate.id)
      || playing.current || current.player.busy()) return;
    const content = current.getCached(candidate.id, candidate.originalLanguage);
    if (!isReady(content, candidate.originalLanguage)) {
      if (!reads.current.has(candidate.id)) {
        reads.current.add(candidate.id);
        const controller = new AbortController(); controllers.current.set(candidate.id, controller);
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
      && !document.hidden && trustedFix(fix.current) && snapshot().eligible[0]?.id === candidate.id
      && allowed(candidate) && !heard.current.has(candidate.id);
    if (!valid()) return;
    attempted.current.add(candidate.id); playing.current = true;
    current.onAutoSelect(candidate);
    void current.player.play(content, valid).then(success => { if (success) heard.current.add(candidate.id); })
      .finally(() => { playing.current = false; });
  }, []);
  const start = useCallback(() => {
    if (!latest.current.settings) { latest.current.onGpsState('error', 'Chưa tải được cấu hình thuyết minh.'); return; }
    if (!window.isSecureContext || !navigator.geolocation) {
      latest.current.onGpsState('error', 'GPS cần HTTPS hoặc localhost và quyền truy cập vị trí.'); return;
    }
    cleanup(); latest.current.player.stop(); latest.current.preparation.resetVisit();
    heard.current.clear(); attempted.current.clear(); reads.current.clear();
    active.current = true; setRunning(true); setReliable(false);
    latest.current.onGpsState('loading', 'Đang tìm vị trí…');
    const session = epoch.current;
    watch.current = navigator.geolocation.watchPosition(position => {
      if (active.current && epoch.current === session) receive(position);
    }, failure => {
      if (!active.current || !alive.current || epoch.current !== session) return;
      fix.current = null; tracker.current.reset(); setReliable(false); latest.current.preparation.stopNearby();
      latest.current.onGpsState(failure.code === 1 ? 'denied' : 'error', failure.code === 1
        ? 'Bạn chưa cấp quyền vị trí. Bạn vẫn có thể khám phá bản đồ và nghe thủ công.' : 'Mất vị trí tin cậy. Đã hủy chờ tự phát.');
      if (failure.code === 1) { cleanup(); setRunning(false); }
    }, { enableHighAccuracy: true, maximumAge: 0, timeout: 15_000 });
    interval.current = setInterval(tick, 250);
  }, [cleanup, receive, tick]);
  useEffect(() => {
    alive.current = true;
    const visibility = () => {
      tracker.current.reset(); choice.current++;
      if (document.hidden) latest.current.preparation.stopNearby();
    };
    document.addEventListener('visibilitychange', visibility);
    return () => { alive.current = false; cleanup(); document.removeEventListener('visibilitychange', visibility); };
  }, [cleanup]);
  return { running, reliable, start, stop, locate, invalidate, notePlayed };
}
