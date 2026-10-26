import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { PoiContent } from '../types';
import { contentKey, isReady, preparationKey, visitorApi } from '../services/api';
import { PreparationClient, type PreparationState } from '../services/preparationClient';

export function useVisitorData(id: number | null, language: string, detailVisible: boolean) {
  const queryClient = useQueryClient();
  const [preparations, setPreparations] = useState<Record<string, PreparationState>>({});
  const [preparation] = useState(() => new PreparationClient((key, state) => {
    setPreparations(current => ({ ...current, [key]: state }));
    const content = state.item?.content;
    if (content) queryClient.setQueryData<PoiContent>(contentKey(content.poiId, content.requestedLanguage), previous =>
      isReady(previous, content.requestedLanguage) && !isReady(content, content.requestedLanguage) ? previous : content);
  }));
  const pois = useQuery({ queryKey: ['pois'], queryFn: ({ signal }) => visitorApi.pois(signal), refetchInterval: 10_000 });
  const languages = useQuery({ queryKey: ['languages'], queryFn: ({ signal }) => visitorApi.languages(signal) });
  const settings = useQuery({ queryKey: ['proximity-settings'], queryFn: ({ signal }) => visitorApi.settings(signal) });
  const detail = useQuery({ queryKey: ['poi', id], queryFn: ({ signal }) => visitorApi.detail(id!, signal), enabled: id !== null && detailVisible, refetchInterval: 10_000 });
  const content = useQuery({ queryKey: contentKey(id ?? 0, language),
    queryFn: ({ signal }) => visitorApi.content(id!, language, signal), enabled: id !== null && detailVisible, refetchInterval: 10_000 });
  useEffect(() => {
    preparation.setInterest('selection', id !== null && detailVisible ? [preparationKey(id, language)] : []);
    return () => preparation.setInterest('selection', []);
  }, [preparation, id, language, detailVisible]);
  useEffect(() => {
    if (id !== null && detailVisible && content.data && !content.isFetching && !isReady(content.data, language))
      void preparation.prepareLanguage(id, language);
  }, [preparation, id, language, detailVisible, content.data, content.isFetching]);
  useEffect(() => {
    if (settings.data) preparation.pollAfterMs = settings.data.pollAfterSeconds * 1000;
  }, [preparation, settings.data]);
  useEffect(() => () => preparation.cancelAll(), [preparation]);
  return { pois, languages, settings, detail, content, preparations, preparation };
}
