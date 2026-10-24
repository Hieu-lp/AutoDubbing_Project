import { demoPois } from '../data/mockPois';
import type { Poi } from '../types';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5133';
const useMockData = import.meta.env.VITE_USE_MOCK_DATA !== 'false';

export async function getPois(): Promise<Poi[]> {
  if (useMockData) return demoPois;

  const response = await fetch(`${apiBaseUrl}/api/PointsOfInterest`);
  if (!response.ok) throw new Error(`Không tải được danh sách địa điểm (${response.status}).`);
  return response.json() as Promise<Poi[]>;
}
