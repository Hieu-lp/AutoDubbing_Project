import { useCallback, useEffect, useRef, useState } from 'react';
import type { PoiContent } from '../types';
import { isReady, preparationKey } from '../services/api';

export function useAudioPlayer(selectionKey: string | null, content?: PoiContent) {
  const ref = useRef<HTMLAudioElement>(null);
  const active = useRef<{ key: string; url: string } | null>(null);
  const sequence = useRef(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [error, setError] = useState('');
  const [speed, setSpeed] = useState(1);
  const invalidatePlay = useCallback(() => { sequence.current++; }, []);
  const stop = useCallback(() => {
    sequence.current++;
    if (ref.current) { ref.current.pause(); ref.current.currentTime = 0; }
    setIsPlaying(false); setCurrentTime(0); setError('');
  }, []);
  const pause = useCallback(() => { sequence.current++; ref.current?.pause(); setIsPlaying(false); }, []);
  useEffect(() => {
    const player = ref.current;
    if (!player) return;
    const update = () => setCurrentTime(Number.isFinite(player.currentTime) ? player.currentTime : 0);
    const loaded = () => setDuration(Number.isFinite(player.duration) ? player.duration : 0);
    const playing = () => setIsPlaying(true);
    const paused = () => setIsPlaying(false);
    const failed = () => { setIsPlaying(false); if (active.current) setError('Không tải được audio. Bạn vẫn có thể đọc nội dung và thử phát lại.'); };
    player.addEventListener('timeupdate', update); player.addEventListener('loadedmetadata', loaded);
    player.addEventListener('play', playing); player.addEventListener('pause', paused);
    player.addEventListener('ended', paused); player.addEventListener('error', failed);
    return () => {
      invalidatePlay(); player.pause(); player.removeAttribute('src'); player.load();
      player.removeEventListener('timeupdate', update); player.removeEventListener('loadedmetadata', loaded);
      player.removeEventListener('play', playing); player.removeEventListener('pause', paused);
      player.removeEventListener('ended', paused); player.removeEventListener('error', failed);
    };
  }, [invalidatePlay]);
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
  const play = useCallback(async (next: PoiContent, valid: () => boolean = () => true): Promise<boolean> => {
    if (!isReady(next, next.requestedLanguage) || !ref.current || !valid()) return false;
    const player = ref.current; const attempt = ++sequence.current;
    const key = preparationKey(next.poiId, next.requestedLanguage); const url = next.audio!.url;
    if (active.current?.key !== key || active.current.url !== url) {
      player.pause(); player.src = url; setCurrentTime(0); setDuration(0); active.current = { key, url };
    }
    setError('');
    try {
      await player.play();
      if (attempt !== sequence.current) return false;
      if (!valid()) { player.pause(); return false; }
      setIsPlaying(true); return true;
    } catch (failure) {
      if (attempt === sequence.current) {
        setIsPlaying(false);
        setError(failure instanceof DOMException && failure.name === 'NotAllowedError'
          ? 'Trình duyệt chặn tự phát. Hãy chạm nút Phát để nghe.' : 'Không phát được audio. Hãy chạm Phát để thử lại.');
      }
      return false;
    }
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
    busy: () => !!ref.current && !ref.current.paused && !ref.current.ended };
}
