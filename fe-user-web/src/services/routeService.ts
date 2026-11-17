import type { Coordinates, Poi, RoutePreview } from '../types.ts';
import { apiRequest } from './api.ts';
import { distanceMeters } from '../tour/proximity';

export function calculateRouteDistances(
  poiIds: number[],
  pois: (Coordinates & { id: number; name: string })[],
) {
  const ordered = poiIds.map(id => pois.find(poi => poi.id === id));
  if (ordered.some(poi => !poi)) return null;

  const legs = ordered.slice(1).map((poi, index) => {
    const from = ordered[index]!;
    const to = poi!;
    return { from, to, distanceMeters: distanceMeters(from, to) };
  });

  return {
    legs,
    totalDistanceMeters: legs.reduce((total, leg) => total + leg.distanceMeters, 0),
  };
}

export function formatRouteDistance(meters: number) {
  return meters < 1000 ? `${Math.round(meters)} m` : `${(meters / 1000).toFixed(2)} km`;
}

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
