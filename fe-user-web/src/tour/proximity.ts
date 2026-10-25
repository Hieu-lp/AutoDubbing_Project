import type { Coordinates, Poi, ProximitySettings, VisitorLocation } from '../types.ts';
import { tourDiagnostic } from './diagnostics.ts';

function positive(value: unknown, fallback: number) { const parsed = Number(value); return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback; }
export const gpsPolicy = {
  maxAgeMs: positive(import.meta.env?.VITE_GPS_MAX_AGE_MS, 10_000),
  maxAccuracyMeters: positive(import.meta.env?.VITE_GPS_MAX_ACCURACY_METERS, 50),
};
export function distanceMeters(a: Coordinates, b: Coordinates) {
  const radians = (n: number) => n * Math.PI / 180;
  const value = Math.sin(radians(b.latitude - a.latitude) / 2) ** 2
    + Math.cos(radians(a.latitude)) * Math.cos(radians(b.latitude)) * Math.sin(radians(b.longitude - a.longitude) / 2) ** 2;
  return 6_371_000 * 2 * Math.atan2(Math.sqrt(Math.max(0, Math.min(1, value))), Math.sqrt(Math.max(0, 1 - value)));
}
function validCoordinates(fix: Coordinates | null) {
  return !!fix && Number.isFinite(fix.latitude) && Math.abs(fix.latitude) <= 90
    && Number.isFinite(fix.longitude) && Math.abs(fix.longitude) <= 180;
}
export function trustedFix(fix: Coordinates | null, now = Date.now()) {
  return validCoordinates(fix) && !!fix
    && Number.isFinite(fix.accuracy) && fix.accuracy! <= gpsPolicy.maxAccuracyMeters && fix.accuracy! >= 0
    && Number.isFinite(fix.timestamp) && now - fix.timestamp! >= -1000 && now - fix.timestamp! <= gpsPolicy.maxAgeMs;
}
/** A chosen map point stays valid until replaced; only GPS fixes expire. */
export function trustedLocation(fix: Coordinates | VisitorLocation | null, now = Date.now()) {
  return validCoordinates(fix) && !!fix && ('source' in fix && fix.source === 'map' || trustedFix(fix, now));
}
export class ProximityTracker {
  private entered = new Map<number, number>();
  private announced = new Set<number>();
  reset(reason = 'location-invalid') {
    if (this.entered.size) tourDiagnostic('dwell-cancel', { ids: [...this.entered.keys()], reason });
    this.entered.clear(); this.announced.clear();
  }
  update(pois: Poi[], fix: Coordinates | VisitorLocation | null, settings: ProximitySettings, now: number, monotonicMs: number) {
    if (!trustedLocation(fix, now)) { this.reset(); return { nearby: [] as Poi[], eligible: [] as Poi[] }; }
    const ordered = pois.map(poi => ({ poi, distance: distanceMeters(fix!, poi) }))
      .sort((a, b) => a.distance - b.distance || a.poi.id - b.poi.id);
    const nearby = ordered.filter(item => item.distance <= settings.preparationRadiusMeters).map(item => item.poi);
    const inside = ordered.filter(item => item.distance <= settings.playbackRadiusMeters);
    const ids = new Set(inside.map(item => item.poi.id));
    for (const id of this.entered.keys()) if (!ids.has(id)) {
      this.entered.delete(id); this.announced.delete(id); tourDiagnostic('dwell-cancel', { ids: [id], reason: 'outside-playback-radius' });
    }
    for (const item of inside) if (!this.entered.has(item.poi.id)) {
      this.entered.set(item.poi.id, monotonicMs);
      tourDiagnostic('dwell-start', { poiId: item.poi.id, distance: item.distance, dwellSeconds: settings.dwellSeconds });
    }
    const eligible = inside.filter(item => monotonicMs - this.entered.get(item.poi.id)! >= settings.dwellSeconds * 1000);
    for (const item of eligible) if (!this.announced.has(item.poi.id)) {
      this.announced.add(item.poi.id); tourDiagnostic('dwell-ready', { poiId: item.poi.id, elapsedMs: monotonicMs - this.entered.get(item.poi.id)! });
    }
    return { nearby, eligible: eligible.map(item => item.poi) };
  }
}
