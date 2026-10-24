export interface AdminProfile {
  id: number;
  username: string;
  fullName: string;
  status: string;
}

export interface AdminLoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  admin: AdminProfile;
}

export interface AdminPoi {
  id: number;
  name: string;
  latitude: number;
  longitude: number;
  proximityRadius: number;
  thumbnailUrl: string | null;
  originalDescription: string;
  originalLanguage: string;
  status: 'Published' | 'Hidden';
}

export type AdminPoiInput = Omit<AdminPoi, 'id'>;

export type DashboardHealth = 'online' | 'warning' | 'offline' | 'unknown';

export interface AdminDashboardResponse {
  generatedAtUtc: string;
  health: { api: DashboardHealth; database: DashboardHealth; pipeline: DashboardHealth };
  pipeline: {
    queued: number;
    processing: number;
    failed: number;
    errorRatePercent: number;
    avgResponseMs: number;
    recentErrors: { component: string; message: string; occurredAtUtc: string }[];
  };
  usage: {
    activeUsers: number;
    autoPlayCount: number;
    manualPlayCount: number;
    listensByPoi: { poiId: number; name: string; count: number }[];
    listensByLanguage: { language: string; count: number }[];
    topRoute: { id: number; name: string; selections: number } | null;
  };
}

const tokenKey = 'voicemap.admin.access-token';
const mockPoisKey = 'voicemap.admin.demo.pois';
const mockToken = 'local-json-admin-demo-token';
export const useAdminMockData = import.meta.env.VITE_USE_MOCK_DATA === 'true'
  || (import.meta.env.DEV && import.meta.env.VITE_USE_MOCK_DATA !== 'false');

async function getMockData() {
  return (await import('../adminMockData.json')).default;
}

function requireDemoToken(token: string) {
  if (token !== mockToken) {
    throw new AdminApiError(401, 'Phiên đăng nhập không hợp lệ.');
  }
}

async function getDemoPois(): Promise<AdminPoi[]> {
  const storedPois = localStorage.getItem(mockPoisKey);
  if (storedPois) {
    try {
      return JSON.parse(storedPois) as AdminPoi[];
    } catch {
      throw new Error('Dữ liệu POI demo trong trình duyệt không hợp lệ. Hãy xóa dữ liệu demo và tải lại trang.');
    }
  }

  const { pois } = await getMockData();
  localStorage.setItem(mockPoisKey, JSON.stringify(pois));
  return pois as AdminPoi[];
}

async function saveDemoPois(pois: AdminPoi[]) {
  localStorage.setItem(mockPoisKey, JSON.stringify(pois));
}

export function getAdminToken() {
  return sessionStorage.getItem(tokenKey);
}

export function saveAdminToken(token: string) {
  sessionStorage.setItem(tokenKey, token);
}

export function clearAdminToken() {
  sessionStorage.removeItem(tokenKey);
}

async function request<T>(path: string, token?: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body) headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(path, { ...init, headers });
  if (!response.ok) {
    let message = `Yêu cầu thất bại (${response.status}).`;
    try {
      const problem = await response.json() as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Keep the HTTP status message when the server has no problem-details body.
    }
    throw new AdminApiError(response.status, message);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export class AdminApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'AdminApiError';
  }
}

export async function loginAdmin(username: string, password: string): Promise<AdminLoginResponse> {
  if (useAdminMockData) {
    const { credentials, admin } = await getMockData();
    if (username !== credentials.username || password !== credentials.password) {
      throw new AdminApiError(401, 'Tên đăng nhập hoặc mật khẩu không chính xác.');
    }
    return {
      accessToken: mockToken,
      tokenType: 'Bearer',
      expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin,
    };
  }

  return request<AdminLoginResponse>('/api/admin/auth/login', undefined, {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  });
}

export async function getAdminProfile(token: string): Promise<AdminProfile> {
  if (useAdminMockData) {
    requireDemoToken(token);
    return (await getMockData()).admin;
  }
  return request<AdminProfile>('/api/admin/auth/me', token);
}

export async function getAdminPois(token: string): Promise<AdminPoi[]> {
  if (useAdminMockData) {
    requireDemoToken(token);
    return getDemoPois();
  }
  return request<AdminPoi[]>('/api/admin/pois', token);
}

export async function createAdminPoi(token: string, poi: AdminPoiInput): Promise<AdminPoi> {
  if (useAdminMockData) {
    requireDemoToken(token);
    const pois = await getDemoPois();
    const created = { ...poi, id: Math.max(0, ...pois.map(({ id }) => id)) + 1 };
    await saveDemoPois([...pois, created]);
    return created;
  }
  return request<AdminPoi>('/api/admin/pois', token, { method: 'POST', body: JSON.stringify(poi) });
}

export async function updateAdminPoi(token: string, id: number, poi: AdminPoiInput): Promise<AdminPoi> {
  if (useAdminMockData) {
    requireDemoToken(token);
    const pois = await getDemoPois();
    if (!pois.some((item) => item.id === id)) throw new AdminApiError(404, 'Không tìm thấy địa điểm.');
    const updated = { ...poi, id };
    await saveDemoPois(pois.map((item) => item.id === id ? updated : item));
    return updated;
  }
  return request<AdminPoi>(`/api/admin/pois/${id}`, token, { method: 'PUT', body: JSON.stringify(poi) });
}

export async function deleteAdminPoi(token: string, id: number): Promise<void> {
  if (useAdminMockData) {
    requireDemoToken(token);
    const pois = await getDemoPois();
    if (!pois.some((item) => item.id === id)) throw new AdminApiError(404, 'Không tìm thấy địa điểm.');
    await saveDemoPois(pois.filter((item) => item.id !== id));
    return;
  }
  return request<void>(`/api/admin/pois/${id}`, token, { method: 'DELETE' });
}

export async function getAdminDashboard(token: string): Promise<AdminDashboardResponse> {
  if (useAdminMockData) {
    requireDemoToken(token);
    return {
      generatedAtUtc: new Date().toISOString(),
      health: { api: 'online', database: 'online', pipeline: 'warning' },
      pipeline: {
        queued: 3,
        processing: 1,
        failed: 1,
        errorRatePercent: 2.4,
        avgResponseMs: 286,
        recentErrors: [{ component: 'Tạo audio', message: 'Audio demo chưa được tạo cho một ngôn ngữ.', occurredAtUtc: new Date(Date.now() - 5 * 60_000).toISOString() }],
      },
      usage: {
        activeUsers: 12,
        autoPlayCount: 45,
        manualPlayCount: 28,
        listensByPoi: [
          { poiId: 1, name: 'Nhà thờ Đức Bà Sài Gòn', count: 36 },
          { poiId: 2, name: 'Bưu điện Trung tâm Sài Gòn', count: 27 },
          { poiId: 3, name: 'Independence Palace', count: 19 },
        ],
        listensByLanguage: [
          { language: 'en', count: 48 },
          { language: 'vi', count: 35 },
          { language: 'ja', count: 16 },
        ],
        topRoute: { id: 1, name: 'Dạo quanh trung tâm Sài Gòn', selections: 18 },
      },
    };
  }

  return request<AdminDashboardResponse>('/api/admin/dashboard/summary?range=24h', token);
}
