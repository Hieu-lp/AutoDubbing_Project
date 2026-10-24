import type { Coordinates, NearbyPreparation, Poi, PoiContent, PreparationItem, ProximitySettings, SupportedLanguage } from '../types.ts';

const apiBase = (import.meta.env?.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '');
export interface ApiResult<T> { data: T; status: number; retryAfterMs: number }
export interface Problem { code?: string; title?: string; detail?: string }
export class ApiError extends Error {
  status: number;
  retryAfterMs: number;
  problem: Problem;
  constructor(status: number, problem: Problem = {}, retryAfterMs = 0) {
    super(problem.detail ?? problem.title ?? 'Không kết nối được API. Vui lòng thử lại.');
    this.name = 'ApiError'; this.status = status; this.problem = problem; this.retryAfterMs = retryAfterMs;
  }
}
export function retryDelay(value: string | null, now = Date.now()): number {
  if (!value) return 0;
  const seconds = Number(value);
  if (Number.isFinite(seconds)) return Math.max(0, seconds * 1000);
  const date = Date.parse(value);
  return Number.isFinite(date) ? Math.max(0, date - now) : 0;
}
export async function apiRequest<T>(path: string, options: {
  signal?: AbortSignal; body?: unknown; allowPreparationError?: boolean; timeoutMs?: number;
} = {}): Promise<ApiResult<T>> {
  const controller = new AbortController();
  const cancel = () => controller.abort();
  options.signal?.addEventListener('abort', cancel, { once: true });
  if (options.signal?.aborted) controller.abort();
  const timeout = setTimeout(cancel, options.timeoutMs ?? 15_000);
  try {
    const response = await fetch(`${apiBase}/${path.replace(/^\//, '')}`, {
      method: options.body === undefined ? 'GET' : 'POST',
      headers: options.body === undefined ? { Accept: 'application/json' }
        : { Accept: 'application/json', 'Content-Type': 'application/json' },
      credentials: 'omit', signal: controller.signal,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
    const data = await response.json().catch(() => null);
    const retryAfterMs = retryDelay(response.headers.get('Retry-After'));
    if (!response.ok && !(options.allowPreparationError && data && (Array.isArray(data.items) || ('poiId' in data && 'status' in data))))
      throw new ApiError(response.status, data ?? {}, retryAfterMs);
    return { data: data as T, status: response.status, retryAfterMs };
  } catch (error) {
    if (options.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
    if (error instanceof ApiError) throw error;
    throw new ApiError(0, { title: controller.signal.aborted ? 'API phản hồi quá chậm. Vui lòng thử lại.' : 'Không kết nối được API. Vui lòng thử lại.' });
  } finally {
    clearTimeout(timeout);
    options.signal?.removeEventListener('abort', cancel);
  }
}
export const contentKey = (id: number, language: string) => ['poi-content', id, language] as const;
export const preparationKey = (id: number, language: string) => `${id}:${language}`;
export function isReady(content: PoiContent | null | undefined, language: string): content is PoiContent {
  return !!content && !content.isFallback && content.actualLanguage === language && !!content.text.trim()
    && content.audio?.language === language && !!content.audio.url;
}
export const visitorApi = {
  pois: (signal?: AbortSignal) => apiRequest<Poi[]>('pois', { signal }).then(r => r.data),
  detail: (id: number, signal?: AbortSignal) => apiRequest<Poi>(`pois/${id}`, { signal }).then(r => r.data),
  languages: (signal?: AbortSignal) => apiRequest<SupportedLanguage[]>('languages', { signal }).then(r => r.data),
  settings: (signal?: AbortSignal) => apiRequest<ProximitySettings>('content-preparations/settings', { signal }).then(r => r.data),
  content: (id: number, language: string, signal?: AbortSignal) =>
    apiRequest<PoiContent>(`pois/${id}/content?language=${encodeURIComponent(language)}`, { signal }).then(r => r.data),
  prepare: (id: number, language: string, signal?: AbortSignal) => apiRequest<PreparationItem>(`pois/${id}/prepare-content`,
    { body: { language }, signal, allowPreparationError: true }),
  nearby: (point: Coordinates, signal?: AbortSignal) => apiRequest<NearbyPreparation>('pois/prepare-nearby',
    { body: { latitude: point.latitude, longitude: point.longitude }, signal, allowPreparationError: true }),
  status: (id: string, signal?: AbortSignal) => apiRequest<PreparationItem>(`content-preparations/${encodeURIComponent(id)}`, { signal }),
};
