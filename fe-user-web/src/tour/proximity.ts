import type { Coordinates, Poi, ProximitySettings } from '../types.ts';

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
export function trustedFix(fix: Coordinates | null, now = Date.now()) {
  return !!fix && Number.isFinite(fix.latitude) && Math.abs(fix.latitude) <= 90
    && Number.isFinite(fix.longitude) && Math.abs(fix.longitude) <= 180
    && Number.isFinite(fix.accuracy) && fix.accuracy! <= gpsPolicy.maxAccuracyMeters && fix.accuracy! >= 0
    && Number.isFinite(fix.timestamp) && now - fix.timestamp! >= -1000 && now - fix.timestamp! <= gpsPolicy.maxAgeMs;
}
export class ProximityTracker {
  private entered = new Map<number, number>();
  reset() { this.entered.clear(); }
  update(pois: Poi[], fix: Coordinates | null, settings: ProximitySettings, now: number, monotonicMs: number) {
    if (!trustedFix(fix, now)) { this.reset(); return { nearby: [] as Poi[], eligible: [] as Poi[] }; }
    const ordered = pois.map(poi => ({ poi, distance: distanceMeters(fix!, poi) }))
      .sort((a, b) => a.distance - b.distance || a.poi.id - b.poi.id);
    const nearby = ordered.filter(item => item.distance <= settings.preparationRadiusMeters).map(item => item.poi);
    const inside = ordered.filter(item => item.distance <= settings.playbackRadiusMeters);
    const ids = new Set(inside.map(item => item.poi.id));
    for (const id of this.entered.keys()) if (!ids.has(id)) this.entered.delete(id);
    for (const id of ids) if (!this.entered.has(id)) this.entered.set(id, monotonicMs);
    return { nearby, eligible: inside.filter(item => monotonicMs - this.entered.get(item.poi.id)! >= settings.dwellSeconds * 1000).map(item => item.poi) };
  }
}
