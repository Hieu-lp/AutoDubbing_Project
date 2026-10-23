import type { Coordinates, Poi } from '../types';

const routingApiUrl = import.meta.env.VITE_ROUTING_API_URL ?? 'https://valhalla1.openstreetmap.de/route';

interface ValhallaRouteResponse {
  trip?: {
    summary?: { length: number; time: number };
    legs?: { shape?: string; summary?: { length: number; time: number } }[];
  };
  error?: string;
  status?: number;
}

export interface WalkingRoute {
  points: [number, number][];
  distanceMeters: number;
  durationSeconds: number;
}

function decodePolyline6(encoded: string): [number, number][] {
  const points: [number, number][] = [];
  let index = 0;
  let latitude = 0;
  let longitude = 0;

  while (index < encoded.length) {
    const deltas: number[] = [];
    for (let coordinate = 0; coordinate < 2; coordinate += 1) {
      let result = 0;
      let shift = 0;
      let byte: number;
      do {
        if (index >= encoded.length) throw new Error('Dữ liệu tuyến đường không hợp lệ.');
        byte = encoded.charCodeAt(index++) - 63;
        result |= (byte & 0x1f) << shift;
        shift += 5;
      } while (byte >= 0x20);
      deltas.push((result & 1) ? ~(result >> 1) : result >> 1);
    }
    latitude += deltas[0];
    longitude += deltas[1];
    points.push([latitude / 1e6, longitude / 1e6]);
  }
  return points;
}

export async function createWalkingRoute(start: Coordinates, stops: Poi[]): Promise<WalkingRoute> {
  if (stops.length < 2) throw new Error('Hãy chọn ít nhất 2 POI để tạo lộ trình.');

  const response = await fetch(routingApiUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      locations: [start, ...stops].map((point) => ({ lat: point.latitude, lon: point.longitude, type: 'break' })),
      costing: 'pedestrian',
      units: 'kilometers',
      directions_options: { units: 'kilometers', language: 'vi-VN' },
    }),
  });

  const result = await response.json() as ValhallaRouteResponse;
  if (!response.ok || result.error || !result.trip?.legs?.length) {
    throw new Error(result.error ?? 'Không lấy được tuyến đi bộ. Vui lòng thử lại.');
  }

  const points = result.trip.legs.flatMap((leg) => leg.shape ? decodePolyline6(leg.shape) : []);
  if (points.length < 2) throw new Error('Dịch vụ không trả về hình dạng tuyến đường hợp lệ.');

  return {
    points,
    distanceMeters: (result.trip.summary?.length ?? result.trip.legs.reduce((sum, leg) => sum + (leg.summary?.length ?? 0), 0)) * 1000,
    durationSeconds: result.trip.summary?.time ?? result.trip.legs.reduce((sum, leg) => sum + (leg.summary?.time ?? 0), 0),
  };
}
