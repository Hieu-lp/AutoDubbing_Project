import type { Coordinates, PreparationItem } from '../types.ts';
import { ApiError, isReady, preparationKey, visitorApi } from './api.ts';
import { tourDiagnostic } from '../tour/diagnostics.ts';

export interface PreparationState { item?: PreparationItem; message?: string; retryAt?: number }
type Dependencies = Pick<typeof visitorApi, 'prepare' | 'nearby' | 'status' | 'content'>;
type Task = { controller: AbortController; promise: Promise<void> };
export function abortableDelay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) { reject(new DOMException('Aborted', 'AbortError')); return; }
    const cancel = () => { clearTimeout(timer); reject(new DOMException('Aborted', 'AbortError')); };
    const timer = setTimeout(() => { signal.removeEventListener('abort', cancel); resolve(); }, ms);
    signal.addEventListener('abort', cancel, { once: true });
  });
}
const pending = (item?: PreparationItem) => item?.status === 'Queued' || item?.status === 'Processing';

/** One coordinator per screen. POSTs are deduplicated; polling only follows current interests. */
export class PreparationClient {
  states = new Map<string, PreparationState>();
  cooldownUntil = 0;
  paused = false;
  pollAfterMs = 3000;
  deadlineMs = 600_000;
  private api: Dependencies;
  private changed: (key: string, state: PreparationState) => void;
  private groups = new Map<string, Set<string>>();
  private tasks = new Map<string, Task>();
  private nearbySeen = new Set<number>();
  private nearbyRequest: { controller: AbortController; added: number[] } | null = null;
  private lastNearbyAt = 0;
  private nearbyDecision = '';
  constructor(changed: (key: string, state: PreparationState) => void, api: Dependencies = visitorApi) {
    this.changed = changed; this.api = api;
  }
  setInterest(group: string, keys: string[]) {
    this.groups.set(group, new Set(keys));
    for (const [key, task] of this.tasks) if (!this.needed(key)) task.controller.abort();
    for (const key of keys) {
      const item = this.states.get(key)?.item;
      const task = this.tasks.get(key);
      if (pending(item) && item?.jobId && !this.states.get(key)?.message
          && (!task || task.controller.signal.aborted) && Date.now() >= this.cooldownUntil)
        void this.run(key, signal => this.poll(item, 0, signal));
    }
  }
  private needed(key: string) { return [...this.groups.values()].some(group => group.has(key)); }
  private emit(key: string, state: PreparationState) { this.states.set(key, state); this.changed(key, state); }
  private accept(item: PreparationItem, retryAfterMs = 0) {
    const key = preparationKey(item.poiId, item.language);
    if (item.errorCode === 'GenerationPaused') this.paused = true;
    const retryAt = item.retryAt ? Date.parse(item.retryAt) : 0;
    if (item.status === 'QuotaExceeded' || item.status === 'QueueFull')
      this.cooldownUntil = Math.max(this.cooldownUntil, retryAt || Date.now() + Math.max(1000, retryAfterMs));
    this.emit(key, { item, retryAt: retryAt || undefined });
  }
  private failure(key: string, error: unknown) {
    if (error instanceof DOMException && error.name === 'AbortError') return;
    if (error instanceof ApiError && error.status === 429) {
      this.cooldownUntil = Math.max(this.cooldownUntil, Date.now() + Math.max(error.retryAfterMs, 1000));
      this.emit(key, { ...this.states.get(key), message: 'Đang giới hạn yêu cầu. Vui lòng chờ rồi thử lại.', retryAt: this.cooldownUntil });
    } else this.emit(key, { ...this.states.get(key), message: error instanceof Error ? error.message : 'Nội dung hiện chưa sẵn sàng' });
  }
  private run(key: string, work: (signal: AbortSignal) => Promise<void>): Promise<void> {
    const existing = this.tasks.get(key);
    if (existing && !existing.controller.signal.aborted) return existing.promise;
    const controller = new AbortController();
    const task: Task = { controller, promise: Promise.resolve() };
    this.tasks.set(key, task);
    task.promise = work(controller.signal).catch(error => {
      if (!controller.signal.aborted) this.failure(key, error);
    }).finally(() => { if (this.tasks.get(key) === task) this.tasks.delete(key); });
    return task.promise;
  }
  async prepareLanguage(id: number, language: string, retry = false): Promise<void> {
    const key = preparationKey(id, language);
    if (!this.needed(key)) return;
    const state = this.states.get(key);
    if (Date.now() < this.cooldownUntil) {
      this.emit(key, { ...state, message: 'Đang giới hạn yêu cầu. Vui lòng chờ rồi thử lại.', retryAt: this.cooldownUntil });
      return;
    }
    if (pending(state?.item) && state?.item?.jobId)
      return this.run(key, signal => this.poll(state.item!, 0, signal));
    if (isReady(state?.item?.content, language) || (state && !retry)) return;
    if (this.paused) { this.emit(key, { ...state, message: generationPausedMessage }); return; }
    return this.run(key, async signal => {
      const result = await this.api.prepare(id, language, signal);
      if (signal.aborted) return;
      this.accept(result.data, result.retryAfterMs);
      if (pending(result.data) && result.data.jobId) await this.poll(result.data, result.retryAfterMs, signal);
    });
  }
  async prepareNearby(point: Coordinates, ids: number[]): Promise<void> {
    // Paused generation still allows Ready content and the status of unseen POIs.
    const reason = !ids.length ? 'no-nearby-pois' : this.nearbyRequest ? 'request-in-flight'
      : !ids.some(id => !this.nearbySeen.has(id)) ? 'already-requested'
      : Date.now() < this.cooldownUntil ? 'rate-limit' : Date.now() - this.lastNearbyAt < 10_000 ? 'throttle' : '';
    const decision = `${ids.join(',')}:${reason}`;
    if (decision !== this.nearbyDecision) {
      this.nearbyDecision = decision;
      if (reason) tourDiagnostic('nearby-skipped', { ids, reason });
    }
    if (reason) return;
    const controller = new AbortController();
    const request = { controller, added: ids.filter(id => !this.nearbySeen.has(id)) };
    this.nearbyRequest = request;
    this.lastNearbyAt = Date.now(); ids.forEach(id => this.nearbySeen.add(id));
    tourDiagnostic('nearby-request', { latitude: point.latitude, longitude: point.longitude, ids, generationPaused: this.paused });
    try {
      const result = await this.api.nearby(point, controller.signal);
      if (controller.signal.aborted) return;
      tourDiagnostic('nearby-response', { status: result.status, items: result.data.items.map(item => ({
        poiId: item.poiId, status: item.status, errorCode: item.errorCode, jobId: item.jobId, audioReady: item.audioReady,
      })) });
      for (const item of result.data.items) {
        this.accept(item, result.retryAfterMs);
        const key = preparationKey(item.poiId, item.language);
        if (pending(item) && item.jobId && this.needed(key))
          void this.run(key, signal => this.poll(item, result.retryAfterMs, signal));
      }
    } catch (error) {
      if (!controller.signal.aborted) for (const id of ids) this.failure(preparationKey(id, 'vi'), error);
    } finally { if (this.nearbyRequest === request) this.nearbyRequest = null; }
  }
  private async poll(initial: PreparationItem, retryAfterMs: number, signal: AbortSignal) {
    const started = Date.now(); const key = preparationKey(initial.poiId, initial.language);
    let item = initial; let wait = Math.max(this.pollAfterMs, retryAfterMs);
    while (pending(item) && item.jobId && !signal.aborted && this.needed(key)) {
      if (Date.now() - started + wait >= this.deadlineMs) {
        this.emit(key, { item, message: 'Đã hết thời gian chờ. Hãy kiểm tra lại nội dung sau.' }); return;
      }
      await abortableDelay(wait, signal);
      try {
        const result = await this.api.status(item.jobId, signal);
        if (signal.aborted) return;
        item = result.data; this.accept(item, result.retryAfterMs);
        wait = Math.max(this.pollAfterMs, result.retryAfterMs);
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          const content = await this.api.content(item.poiId, item.language, signal);
          if (signal.aborted) return;
          this.accept({ ...item, status: isReady(content, item.language) ? 'Ready' : 'Missing',
            audioReady: isReady(content, item.language), textReady: !content.isFallback,
            content, jobId: null, statusUrl: null, errorCode: 'JobNotFound' });
          return;
        }
        throw error;
      }
    }
  }
  stopNearby(newPosition = false) {
    if (this.nearbyRequest) {
      this.nearbyRequest.controller.abort();
      for (const id of this.nearbyRequest.added) this.nearbySeen.delete(id);
      this.nearbyRequest = null; this.lastNearbyAt = 0;
    }
    if (newPosition) this.lastNearbyAt = 0;
    this.setInterest('nearby', []);
  }
  resetVisit() { this.nearbySeen.clear(); this.lastNearbyAt = 0; }
  cancelAll() {
    for (const task of this.tasks.values()) task.controller.abort();
    this.stopNearby(); this.groups.clear();
  }
}

export const generationPausedMessage = 'Nội dung hiện chưa sẵn sàng: chưa có audio, tính năng tạo nội dung đang tạm dừng. Bạn vẫn có thể đọc mô tả.';
export function preparationMessage(state?: PreparationState): string {
  if (state?.message) return state.message;
  const item = state?.item;
  if (!item || item.status === 'Ready') return '';
  if (pending(item) && item.jobId) return 'Đang chuẩn bị nội dung…';
  if (item.errorCode === 'GenerationPaused') return generationPausedMessage;
  if (item.requiresOperatorAction) return 'Nội dung hiện chưa sẵn sàng. Dịch vụ cần được kiểm tra.';
  if (item.status === 'QuotaExceeded' || item.status === 'QueueFull') return 'Nội dung hiện chưa sẵn sàng. Vui lòng chờ rồi thử lại.';
  return 'Nội dung hiện chưa sẵn sàng';
}
