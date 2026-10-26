import { useCallback, useEffect, useRef, useState } from 'react';
import type { PoiContent } from '../types';
import { isReady, preparationKey } from '../services/api';
import { tourDiagnostic } from '../tour/diagnostics';
import { eventId, recordPlayback } from '../services/usageService';

export function useAudioPlayer(selectionKey: string | null, content?: PoiContent, trackUsage = false) {
  const ref = useRef<HTMLAudioElement>(null);
  const active = useRef<{ key: string; url: string } | null>(null);
  const sequence = useRef(0);
  const pending = useRef<number | null>(null);
  const listening = useRef<{ id: string; content: PoiContent; kind: 'auto' | 'manual'; sent: boolean; valid: () => boolean } | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [error, setError] = useState('');
  const [speed, setSpeed] = useState(1);
  const invalidatePlay = useCallback(() => { sequence.current++; }, []);
  const cancelPending = useCallback(() => {
    if (pending.current === null) return;
    sequence.current++; pending.current = null; ref.current?.pause(); setIsPlaying(false);
  }, []);
  const stop = useCallback(() => {
    listening.current = null;
    sequence.current++; pending.current = null;
    if (ref.current) { ref.current.pause(); ref.current.currentTime = 0; }
    setIsPlaying(false); setCurrentTime(0); setError('');
  }, []);
  const pause = useCallback(() => { sequence.current++; pending.current = null; ref.current?.pause(); setIsPlaying(false); }, []);
  useEffect(() => {
    const player = ref.current;
    if (!player) return;
    const update = () => setCurrentTime(Number.isFinite(player.currentTime) ? player.currentTime : 0);
    const loaded = () => setDuration(Number.isFinite(player.duration) ? player.duration : 0);
    const playing = () => setIsPlaying(true);
    const began = () => {
      const attempt = listening.current;
      if (trackUsage && attempt && !attempt.sent && attempt.valid() && active.current?.key === preparationKey(attempt.content.poiId, attempt.content.requestedLanguage)) {
        attempt.sent = true; void recordPlayback(attempt.content, attempt.kind, attempt.id);
      }
    };
    const paused = () => setIsPlaying(false);
    const failed = () => { setIsPlaying(false); if (active.current) setError('Không tải được audio. Bạn vẫn có thể đọc nội dung và thử phát lại.'); };
    player.addEventListener('timeupdate', update); player.addEventListener('loadedmetadata', loaded);
    player.addEventListener('play', playing); player.addEventListener('pause', paused);
    player.addEventListener('playing', began);
    player.addEventListener('ended', paused); player.addEventListener('error', failed);
    return () => {
      invalidatePlay(); player.pause(); player.removeAttribute('src'); player.load();
      player.removeEventListener('timeupdate', update); player.removeEventListener('loadedmetadata', loaded);
      player.removeEventListener('play', playing); player.removeEventListener('pause', paused);
      player.removeEventListener('playing', began);
      player.removeEventListener('ended', paused); player.removeEventListener('error', failed);
    };
  }, [invalidatePlay, trackUsage]);
  useEffect(() => {
    if (active.current?.key !== selectionKey) { stop(); active.current = null; setDuration(0); }
  }, [selectionKey, stop]);
  useEffect(() => {
    const player = ref.current;
    if (!player || !content) return;
    if (!isReady(content, content.requestedLanguage)) {
      if (active.current?.key === selectionKey) { stop(); active.current = null; player.removeAttribute('src'); setDuration(0); }
      return;
    }
    const key = preparationKey(content.poiId, content.requestedLanguage);
    if (key === selectionKey && (active.current?.key !== key || active.current.url !== content.audio!.url)) {
      stop(); setDuration(0); active.current = { key, url: content.audio!.url }; player.src = content.audio!.url;
    }
  }, [content, selectionKey, stop]);
  const play = useCallback(async (next: PoiContent, valid: () => boolean = () => true, kind: 'auto' | 'manual' = 'manual'): Promise<boolean> => {
    if (!isReady(next, next.requestedLanguage) || !ref.current || !valid()) return false;
    const player = ref.current; const attempt = ++sequence.current;
    const key = preparationKey(next.poiId, next.requestedLanguage); const url = next.audio!.url;
    if (active.current?.key !== key || active.current.url !== url) {
      player.pause(); player.src = url; setCurrentTime(0); setDuration(0); active.current = { key, url };
    }
    setError('');
    listening.current = { id: eventId(), content: next, kind, sent: false, valid };
    pending.current = attempt;
    tourDiagnostic('audio-play', { poiId: next.poiId, language: next.actualLanguage, path: new URL(url, location.href).pathname });
    try {
      await player.play();
      if (attempt !== sequence.current) return false;
      if (!valid()) { player.pause(); return false; }
      setIsPlaying(true); tourDiagnostic('audio-playing', { poiId: next.poiId }); return true;
    } catch (failure) {
      if (attempt === sequence.current) {
        tourDiagnostic('audio-rejected', { poiId: next.poiId, name: failure instanceof Error ? failure.name : 'UnknownError' });
        setIsPlaying(false);
        setError(failure instanceof DOMException && failure.name === 'NotAllowedError'
          ? 'Trình duyệt chặn tự phát. Hãy chạm nút Phát để nghe.' : 'Không phát được audio. Hãy chạm Phát để thử lại.');
      }
      return false;
    } finally { if (pending.current === attempt) pending.current = null; }
  }, []);
  const seek = useCallback((time: number) => {
    if (ref.current && Number.isFinite(ref.current.duration))
      ref.current.currentTime = Math.max(0, Math.min(time, ref.current.duration));
  }, []);
  const cycleSpeed = useCallback(() => {
    if (!ref.current) return;
    const value = ref.current.playbackRate >= 1.5 ? 0.75 : ref.current.playbackRate + 0.25;
    ref.current.playbackRate = value; setSpeed(value);
  }, []);
  return { ref, isPlaying, currentTime, duration, error, speed, stop, pause, play, seek, cycleSpeed,
    cancelPending, busy: () => !!ref.current && !ref.current.paused && !ref.current.ended };
}
