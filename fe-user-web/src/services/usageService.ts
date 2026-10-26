import { apiRequest } from './api.ts';
import type { PoiContent } from '../types.ts';

export function eventId(): string {
  if (crypto.randomUUID) return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
let memoryGuest: string | undefined;
export function guestId(): string {
  if (memoryGuest) return memoryGuest;
  try {
    const stored = sessionStorage.getItem('voicemap.visitor-id');
    if (stored && /^[a-f\d]{8}-(?:[a-f\d]{4}-){3}[a-f\d]{12}$/i.test(stored)) return memoryGuest = stored;
  } catch { /* Private storage can be unavailable; keep this visit in memory. */ }
  memoryGuest = eventId();
  try { sessionStorage.setItem('voicemap.visitor-id', memoryGuest); } catch { /* Playback does not depend on storage. */ }
  return memoryGuest;
}
async function record(body: object) {
  try { await apiRequest('usage-events', { body: { ...body, guestId: guestId() } }); }
  catch { /* Statistics failures must never interrupt playback or routing. */ }
}
export function recordPlayback(content: PoiContent, kind: 'auto' | 'manual', id: string) {
  return record({ eventId: id, eventType: `${kind}_play`, poiId: content.poiId, language: content.actualLanguage, audioId: content.audio?.id });
}
export function recordRouteSelection(routeId: number) { return record({ eventId: eventId(), eventType: 'route_selection', routeId }); }
export function startHeartbeat() {
  const beat = () => { if (!document.hidden) void apiRequest('visitor-heartbeat', { body: { guestId: guestId() } }).catch(() => {}); };
  beat(); const timer = setInterval(beat, 30_000); document.addEventListener('visibilitychange', beat);
  return () => { clearInterval(timer); document.removeEventListener('visibilitychange', beat); };
}
