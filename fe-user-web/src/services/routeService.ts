import type { Coordinates, Poi, RoutePreview } from '../types.ts';
import { apiRequest } from './api.ts';

export interface WalkingRoute {
  points: [number, number][];
  distanceMeters: number;
  durationSeconds: number;
  stops: RoutePreview['stops'];
  attribution: string;
}
export async function createWalkingRoute(start: Coordinates, stops: Poi[], signal?: AbortSignal, savedRouteId?: number): Promise<WalkingRoute> {
  if (!savedRouteId && !stops.length) throw new Error('Chọn ít nhất một POI để tạo lộ trình.');
  const { data } = await apiRequest<RoutePreview>(savedRouteId ? `routes/${savedRouteId}/preview` : 'routes/preview', {
    body: { startLatitude: start.latitude, startLongitude: start.longitude, ...(savedRouteId ? {} : { poiIds: stops.map(poi => poi.id) }) },
    signal, timeoutMs: 30_000,
  });
  if (data.geometry?.type !== 'LineString' || data.geometry.coordinates.length < 2
      || !Number.isFinite(data.totalDistanceMeters) || !Number.isFinite(data.totalDurationSeconds)
      || data.geometry.coordinates.some(point => point.length < 2 || !point.every(Number.isFinite)))
    throw new Error('Backend chưa trả về tuyến đi bộ hợp lệ.');
  return {
    // GeoJSON is longitude,latitude; Leaflet receives latitude,longitude.
    points: data.geometry.coordinates.map(([longitude, latitude]) => [latitude, longitude]),
    distanceMeters: data.totalDistanceMeters, durationSeconds: data.totalDurationSeconds,
    stops: data.stops, attribution: data.attribution,
  };
}
