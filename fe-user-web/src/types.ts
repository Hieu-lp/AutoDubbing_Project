export type Language =
  | 'vi' | 'en' | 'zh-hans' | 'ja' | 'ko' | 'fr' | 'de' | 'es'
  | 'ru' | 'it' | 'pt' | 'th' | 'id' | 'ms' | 'hi';

export interface Poi {
  id: number;
  name: string;
  latitude: number;
  longitude: number;
  proximityRadius: number;
  thumbnailUrl: string | null;
  originalDescription?: string;
  originalLanguage: Language;
  /** Legacy fixtures only. Visitor UI reads the backend PoiContent contract. */
  descriptions?: Partial<Record<Language, string>>;
  audioUrls?: Partial<Record<Language, string>>;
  category?: string;
}

export interface Coordinates {
  latitude: number;
  longitude: number;
  accuracy?: number;
  timestamp?: number;
}

export interface SavedRoute {
  id: number; name: string; poiIds: number[]; status: 'Published' | 'Hidden'; updatedAtUtc: string;
  pois: { id: number; name: string; latitude: number; longitude: number; isPublic: boolean }[];
  unavailablePoiIds: number[]; version: string;
}

export type VisitorLocation = Coordinates & { source: 'gps' | 'map' };

export interface NearbyPoi extends Poi {
  distance: number;
}

export interface SupportedLanguage { code: Language; displayName: string }
export interface PoiContent {
  poiId: number;
  requestedLanguage: string;
  actualLanguage: string;
  isFallback: boolean;
  text: string;
  audio: { id: number; language: string; url: string } | null;
}
export interface ProximitySettings {
  preparationRadiusMeters: number;
  playbackRadiusMeters: number;
  dwellSeconds: number;
  pollAfterSeconds: number;
}
export type PreparationStatus = 'Ready' | 'Queued' | 'Processing' | 'Missing' | 'QuotaExceeded'
  | 'QueueFull' | 'Unavailable' | 'Failed' | 'Superseded' | 'Cancelled';
export interface PreparationItem {
  poiId: number;
  language: string;
  sourceVersion: string;
  status: PreparationStatus;
  textReady: boolean;
  audioReady: boolean;
  jobId: string | null;
  statusUrl: string | null;
  errorCode: string | null;
  retryAt: string | null;
  requiresOperatorAction: boolean;
  content: PoiContent | null;
  distanceMeters?: number;
}
export interface NearbyPreparation { preparationRadiusMeters: number; items: PreparationItem[] }
export interface RoutePreview {
  start: Coordinates;
  stops: { order: number; poiId: number; name: string; latitude: number; longitude: number;
    distanceFromPreviousMeters: number; durationFromPreviousSeconds: number }[];
  totalDistanceMeters: number;
  totalDurationSeconds: number;
  geometry: { type: 'LineString'; coordinates: [number, number][] };
  algorithm: string;
  distanceType: string;
  provider: string;
  attribution: string;
}
