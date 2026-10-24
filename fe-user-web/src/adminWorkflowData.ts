export type WorkflowStatus = 'draft' | 'review' | 'approved';
export type RouteStatus = 'draft' | 'published';
export type SessionStatus = 'draft' | 'published' | 'closed';

export interface NarrationDraft {
  poiId: number;
  language: string;
  text: string;
  status: WorkflowStatus;
  updatedAt: string;
}

export interface DemoRoute {
  id: string;
  name: string;
  poiIds: number[];
  status: RouteStatus;
  updatedAt: string;
}

export interface DemoSession {
  id: string;
  name: string;
  description: string;
  routeIds: string[];
  status: SessionStatus;
  updatedAt: string;
}

export const narrationLanguages = [
  { code: 'vi', label: 'Tiếng Việt' },
  { code: 'en', label: 'English' },
  { code: 'zh-hans', label: '中文（简体）' },
  { code: 'ja', label: '日本語' },
  { code: 'ko', label: '한국어' },
  { code: 'fr', label: 'Français' },
  { code: 'de', label: 'Deutsch' },
  { code: 'es', label: 'Español' },
  { code: 'ru', label: 'Русский' },
  { code: 'it', label: 'Italiano' },
  { code: 'pt', label: 'Português (Brasil)' },
  { code: 'th', label: 'ไทย' },
  { code: 'id', label: 'Bahasa Indonesia' },
  { code: 'ms', label: 'Bahasa Melayu' },
  { code: 'hi', label: 'हिन्दी' },
] as const;

const narrationKey = 'voicemap.admin.demo.narrations';
const routesKey = 'voicemap.admin.demo.routes';
const sessionsKey = 'voicemap.admin.demo.sessions';
export const workflowChangeEvent = 'voicemap:admin-demo-workflow-changed';

function readCollection<T>(key: string): T[] {
  try {
    const value = localStorage.getItem(key);
    return value ? JSON.parse(value) as T[] : [];
  } catch {
    return [];
  }
}

function writeCollection<T>(key: string, value: T[]) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
    window.dispatchEvent(new Event(workflowChangeEvent));
    return true;
  } catch {
    return false;
  }
}

export const loadNarrations = () => readCollection<NarrationDraft>(narrationKey);
export const saveNarrations = (value: NarrationDraft[]) => writeCollection(narrationKey, value);
export const loadRoutes = () => readCollection<DemoRoute>(routesKey);
export const saveRoutes = (value: DemoRoute[]) => writeCollection(routesKey, value);
export const loadSessions = () => readCollection<DemoSession>(sessionsKey);
export const saveSessions = (value: DemoSession[]) => writeCollection(sessionsKey, value);

export function formatUpdatedAt(value: string) {
  return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}
