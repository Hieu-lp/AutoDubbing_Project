import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from './services/api';
import type { SavedRoute } from './types';
import { calculateRouteDistances, formatRouteDistance } from './services/routeService';

export default function SuggestedRoutes({ selected, onSelect, onChanged }: {
  selected: SavedRoute | null; onSelect: (route: SavedRoute) => void; onChanged: (route: SavedRoute | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const query = useQuery({ queryKey: ['suggested-routes'], enabled: open || !!selected,
    queryFn: ({ signal }) => apiRequest<SavedRoute[]>('routes', { signal }).then(result => result.data),
    staleTime: 0, refetchOnWindowFocus: true, refetchInterval: 10_000 });
  const selectedDistances = selected ? calculateRouteDistances(selected.poiIds, selected.pois) : null;
  useEffect(() => {
    if (!selected || !query.isSuccess || query.isFetching) return;
    const current = query.data.find(route => route.id === selected.id);
    if (!current) onChanged(null);
    else if (current.version !== selected.version) onChanged(current);
  }, [selected, query.data, query.isSuccess, query.isFetching, onChanged]);
  return <section className="suggested-routes" aria-label="Lộ trình gợi ý">
    <button type="button" className="plan-route" aria-expanded={open} onClick={() => setOpen(value => !value)}>Lộ trình gợi ý</button>
    {selected && <p role="status">Đã chọn: {selected.name} · giữ thứ tự của quản trị viên.</p>}
    {selected && <ol aria-label="Thứ tự POI của lộ trình">{selected.poiIds.map((id, index) => <li key={id}>
      {selected.pois.find(poi => poi.id === id)?.name ?? `POI ${id} chưa xác minh`}
      {index > 0 && selectedDistances && <small> · Cách điểm trước: {formatRouteDistance(selectedDistances.legs[index - 1].distanceMeters)}</small>}
    </li>)}</ol>}
    {selectedDistances && <p>Tổng khoảng cách giữa các POI: <strong>{formatRouteDistance(selectedDistances.totalDistanceMeters)}</strong> · Đường chim bay</p>}
    {open && <div className="suggested-route-list">
      <p>Lộ trình đã xuất bản · giữ đúng thứ tự POI · cập nhật mỗi 10 giây.</p>
      <button type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>Làm mới gợi ý</button>
      {query.isPending && <p role="status">Đang tải lộ trình gợi ý…</p>}
      {query.isError && <p role="alert">Không tải được lộ trình gợi ý.{query.data && ' Đang giữ danh sách đọc gần nhất; chưa xác minh được thay đổi mới.'}</p>}
      {query.isSuccess && query.data.length === 0 && <p>Chưa có lộ trình gợi ý được xuất bản.</p>}
      {query.data?.map(route => {
        const distances = calculateRouteDistances(route.poiIds, route.pois);
        return <button type="button" className="route-choice" key={route.id} aria-pressed={selected?.id === route.id} onClick={() => onSelect(route)}>
          <span><strong>{route.name}</strong><small>{route.pois.map(poi => poi.name).join(' → ')}</small>
            {distances && <small>Tổng: {formatRouteDistance(distances.totalDistanceMeters)} · Đường chim bay</small>}
          </span>
        </button>;
      })}
    </div>}
  </section>;
}
