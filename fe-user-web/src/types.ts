export type Language = 'vi' | 'en';

export interface Poi {
  id: number;
  name: string;
  latitude: number;
  longitude: number;
  proximityRadius: number;
  thumbnailUrl: string | null;
  originalDescription: string;
  originalLanguage: Language;
  descriptions: Record<Language, string>;
  audioUrls: Partial<Record<Language, string>>;
  category: string;
}

export interface Coordinates {
  latitude: number;
  longitude: number;
  accuracy?: number;
}

export interface NearbyPoi extends Poi {
  distance: number;
}
