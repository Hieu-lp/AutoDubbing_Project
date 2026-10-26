# Tích hợp web du khách — 25/10/2026

Đã nối frontend có sẵn trong [fe-user-web](../fe-user-web) với API/Service/Data hiện hành trên nhánh `frontend-user-web`. Giữ bố cục và component bản đồ/danh sách/chi tiết/audio/lộ trình của đồng đội. Không sửa backend, schema, dữ liệu POI, bản dịch, audio hoặc object Supabase; không commit/push/merge.

## Chức năng và file thay đổi

| Phần | File | Hành vi |
| --- | --- | --- |
| Màn hình du khách | [App.tsx](../fe-user-web/src/App.tsx), [integration.css](../fe-user-web/src/integration.css) | POI/marker thật; chi tiết, 15 ngôn ngữ từ API; nhãn fallback; chọn vị trí, nhiều POI, phát/dừng/seek/tốc độ; giữ responsive |
| Đọc dữ liệu | [api.ts](../fe-user-web/src/services/api.ts), [useVisitorData.ts](../fe-user-web/src/hooks/useVisitorData.ts), [types.ts](../fe-user-web/src/types.ts), [main.tsx](../fe-user-web/src/main.tsx) | DTO theo controller/OpenAPI; request ẩn danh, AbortSignal, timeout hữu hạn; cache theo POI/ngôn ngữ; response cũ không đổi lựa chọn mới |
| Chuẩn bị nội dung | [preparationClient.ts](../fe-user-web/src/services/preparationClient.ts) | Gộp/chống trùng POST; dùng Ready ngay; xử lý 503 GenerationPaused, 429/Retry-After, job 404 và polling 202 tối đa 10 phút; hủy theo vùng/lựa chọn/trang |
| Lộ trình | [routeService.ts](../fe-user-web/src/services/routeService.ts) | Chỉ POST backend; dùng toàn bộ GeoJSON, thứ tự, khoảng cách/thời gian đi bộ và attribution backend trả; lỗi không dựng đường thay thế |
| GPS/audio | [useTourGuide.ts](../fe-user-web/src/hooks/useTourGuide.ts), [proximity.ts](../fe-user-web/src/tour/proximity.ts), [useAudioPlayer.ts](../fe-user-web/src/hooks/useAudioPlayer.ts) | Watcher khi chủ động bắt đầu; settings 500 m/50 m/3 giây; fix mới và đủ chính xác; kiểm tra lại trước play; một player, tối đa một lần tự phát/POI/phiên; dọn tài nguyên |
| Chạy/kiểm tra | [.env.example](../fe-user-web/.env.example), [vite.config.ts](../fe-user-web/vite.config.ts), [package.json](../fe-user-web/package.json), [package-lock.json](../fe-user-web/package-lock.json), [.gitignore](../fe-user-web/.gitignore), [eslint.config.js](../fe-user-web/eslint.config.js), [playwright.config.ts](../fe-user-web/playwright.config.ts), [tests](../fe-user-web/tests), [e2e](../fe-user-web/e2e) | Proxy HTTPS IPv4/CA hệ thống; cấu hình công khai; lint/unit/Edge; mặc định mock, live bị chặn trong CI |
| Hướng dẫn | [README](../fe-user-web/README.md), [hợp đồng nội dung](content-on-demand.md), tài liệu này | Lệnh chạy, cấu hình, demo và phân biệt kiểm chứng thật/mô phỏng |

File mock POI cũ được giữ nguyên nhưng không còn được import trong luồng chạy. Không thêm key provider/JWT vào frontend. Không cần sửa CORS khi dùng Vite proxy cùng origin.

## Hợp đồng frontend đang sử dụng

| Request | Cách dùng |
| --- | --- |
| `GET /api/pois`, `GET /api/pois/{id}` | Danh sách Published, tọa độ, chi tiết từ backend; không lọc bỏ POI chỉ vì chưa cấp GPS |
| `GET /api/languages` | 15 mã thực tế, kể cả `zh-hans`; không tự tạo danh mục riêng |
| `GET /api/pois/{id}/content?language=...` | Chỉ đọc. Hiện `actualLanguage` khi fallback; fallback không được coi là audio của ngoại ngữ đã chọn |
| `GET /api/content-preparations/settings` | Ngưỡng chuẩn bị/phát/dwell/poll; không đổi bán kính SQL |
| `POST /api/pois/{id}/prepare-content` với `{language}` | Chỉ khi phần yêu cầu chưa Ready. 200 dùng ngay; 202 theo job; 503 giữ phần đọc được; 429 chờ Retry-After |
| `POST /api/pois/prepare-nearby` với `{latitude,longitude}` | Gửi khi có POI mới trong vùng, không gửi từng GPS tick; backend chuẩn bị nguồn, không tạo mọi ngoại ngữ |
| `GET /api/content-preparations/{jobId}` | Polling hữu hạn, hủy khi không còn quan tâm. Sau job 404 chỉ đọc lại nội dung, không tự tạo job mới |
| `POST /api/routes/preview` với `{startLatitude,startLongitude,poiIds}` | Backend xếp thứ tự gần trước. Dùng `geometry.coordinates`, `totalDistanceMeters`, `totalDurationSeconds`, `stops`, `attribution`; frontend đổi lng/lat thành lat/lng cho Leaflet |

Sau GenerationPaused, manager giữ trạng thái paused trong vòng đời trang. Có thể đọc/nghe nội dung sẵn có; tải lại trang sau khi người vận hành thay đổi cấu hình backend. Không có công tắc frontend để bật generation. Khi chọn ngoại ngữ, chỉ audio đúng ngôn ngữ được thử phát; nghe fallback nguồn là hành động rõ ràng riêng.

GPS có chính sách client mặc định fix không quá 10 giây, sai số không quá 50 m. Tọa độ chọn trên map chỉ dùng khám phá/lập tuyến, không dùng tự phát. Ra vùng/mất fix trước dwell hủy chờ; audio về muộn phải kiểm tra lại vị trí, lựa chọn và phiên. Dừng rồi bắt đầu lại mở một lượt tham quan mới. Autoplay bị browser chặn thì hiện nút/hướng dẫn Phát; không gọi play lặp liên tục.

## Kết quả kiểm chứng

API local được build từ source hiện tại, khởi động đúng workspace; cấu hình hiệu lực **`ContentPreparation:AcceptNewGeneration=false`**, xác minh từ process command line trước POST và sau kiểm tra. Không gọi Gemini hoặc chạy batch. Chỉ endpoint đọc, prepare đang paused và một preview ORS thật được dùng.

| Kiểm tra | Kết quả |
| --- | --- |
| Backend build | 0 lỗi, 0 warning; output riêng trong artifacts, không thay source backend |
| Frontend lint | Đạt, không lỗi/warning |
| Frontend unit | **20/20**: hợp đồng HTTP, cache/fallback, chống trùng, polling/hủy/deadline, Retry-After, GeoJSON và GPS/dwell |
| Frontend production build | Đạt TypeScript + Vite; tách map/vendor, không warning chunk lớn |
| Edge với mock | **10/10**: POI/audio, paused/fallback, đổi nhanh, route/error, mobile, GPS denied/stale/ra vùng, autoplay bị chặn, 202 muộn và 429 |
| Edge với API thật | **5 trường hợp đã đạt**: 8 POI/marker và 15 audio Nhà thờ; missing/fallback; ORS; GPS mô phỏng; mobile |

Lượt live đầu đạt 4/5; ca đầu bị timeout tải danh sách và hiển thị trạng thái lỗi có nút thử lại. Trace ghi request POI bị hủy/không có response; không đủ bằng chứng quy nguyên nhân cho SQL hoặc mạng. Chạy lại riêng ca đọc 8 POI/15 audio đạt 1/1. Không tăng timeout hoặc bật retry hàng loạt để che lỗi đó. Hai báo cáo được giữ để kết quả có thể đối chiếu.

Kết quả thật cụ thể:

- Nhà thờ Đức Bà: 15 lựa chọn từ API, nội dung không fallback, WAV tải metadata được trong Edge, thời lượng **32,92–43,88 giây**, thao tác play/pause thành công. Luồng đọc 15 ngôn ngữ gửi **0 POST**, tái sử dụng dữ liệu đã lưu. Trình duyệt kiểm tra chạy tắt tiếng; đây không phải thẩm định giọng đọc/bản dịch.
- Bưu điện: audio nguồn thiếu, POST trả **503 / Unavailable / GenerationPaused**, `textReady:true`, `audioReady:false`, `jobId:null`; vẫn đọc mô tả. Chọn en hiển thị rõ fallback vi. Không lặp POST sau paused.
- Preview qua backend/ORS trả **200**, `walking`, `foot-walking`, `LineString` **57 đỉnh**, thứ tự Nhà thờ → Bưu điện, **1.788,4 m / 1.287,6 giây**. UI vẽ đường theo phố và dùng số liệu backend. Điểm xuất phát là điểm chọn trên map trong bài kiểm tra, không phải vị trí thực địa.
- GPS trình duyệt **mô phỏng** ở `10.7798,106.699`, accuracy 5 m: một POST nearby, không POST prepare-content, sau dwell phát nguồn vi đã lưu; dừng phiên thành công. Không gọi đây là kiểm chứng GPS ngoài thực địa.
- Mobile Edge viewport **390×844**, nội dung API thật; không tràn ngang. Đã xem ảnh desktop/mobile/lộ trình.
- SQL snapshot trước/sau **giống nhau**: 8 POI Published, 14 bản dịch Ready, 15 audio Ready; user 1; sessions/routes/usageEvents/systemLogs đều 0. Nội dung, ID và metadata đã kiểm kê không thay đổi. Cache snapshot cũng giống nhau (0 file ở hai đường dẫn cache được cấu hình/kiểm kê). Không upload, xóa hoặc chỉnh object Supabase.

Bằng chứng local nằm trong thư mục artifacts bị git ignore: [so sánh SQL/cache](../artifacts/frontend-integration/comparison.json), [cờ runtime](../artifacts/frontend-integration/runtime-proof.json), [15 metadata audio](../artifacts/frontend-integration/audio-live.json), [paused](../artifacts/frontend-integration/paused-live.json), [ORS](../artifacts/frontend-integration/route-live.json), [GPS mô phỏng](../artifacts/frontend-integration/gps-simulated-live.json), [ảnh desktop](../artifacts/frontend-integration/desktop-live.png), [ảnh mobile](../artifacts/frontend-integration/mobile-live.png), [ảnh tuyến](../artifacts/frontend-integration/route-live.png). Báo cáo Edge: [mock 10 ca](../artifacts/frontend-integration/mock-playwright-report/index.html), [live lượt đầu](../artifacts/frontend-integration/live-initial-report/index.html), [live audio chạy lại](../artifacts/frontend-integration/live-audio-report/index.html). Các artifact này không tự có khi clone repo mới.

## Chạy và tự demo

Xem hướng dẫn đầy đủ tại [README frontend](../fe-user-web/README.md). Hai server hiện phục vụ frontend **http://localhost:5173**, backend **https://localhost:7239**.

```powershell
# Terminal 1, repository root; chỉ chạy nếu API chưa chạy trên cổng đó
dotnet run --project AutoDubbing.API --launch-profile https -- --ContentPreparation:AcceptNewGeneration=false

# Terminal 2
cd fe-user-web
npm ci
if (!(Test-Path .env.local)) { Copy-Item .env.example .env.local }
npm run dev
```

1. Mở web, chọn marker/danh sách Nhà thờ, thử 15 ngôn ngữ và play/pause/seek.
2. Mở Bưu điện và chọn en để xem nội dung thiếu/fallback/paused.
3. Chọn vị trí trên map, Tạo lộ trình, chọn hai điểm và tạo; kiểm tra đường, khoảng cách/thời gian.
4. DevTools Sensors mô phỏng tọa độ Nhà thờ, cấp quyền GPS, bấm Bắt đầu thuyết minh. Chờ đủ 3 giây, chạm Phát nếu browser chặn. Dừng thuyết minh khi xong.

Proxy dùng HTTPS IPv4 và CA hệ thống, không bỏ kiểm tra TLS. Node hiện kiểm chứng **22.20.0**. Không đưa key vào `.env.local` frontend. Khi demo điện thoại qua LAN, GPS cần HTTPS phù hợp; HTTP địa chỉ LAN không đáp ứng secure context.

## Giới hạn còn lại

- **202 → Ready sinh mới chỉ được test mock**, không kiểm chứng pipeline/provider thật do giữ generation paused/quota đang bị chặn. Không tự bật billing hoặc đổi model/key.
- ORS thành công đã kiểm chứng thật; các nhánh lỗi ORS/429, response đảo thứ tự và polling hủy được chủ động dựng bằng mock. Không gây lỗi/quá tải provider thật để thử.
- Cần người dùng kiểm tra GPS ngoài thực địa, cảm nhận giọng đọc/bản dịch, audio trên thiết bị iOS/Android và chính sách autoplay của từng browser. Thử nghiệm desktop Edge không thay cho các kiểm tra đó.
- Giới hạn backend giữ nguyên: **một instance**; queue/job/cooldown trong bộ nhớ mất khi restart; bản dịch lịch sử chưa có revision nguồn trong schema. Client không khắc phục được các giới hạn này.
- Vite dev/preview dành cho local; production cần HTTPS và reverse proxy `/api` theo môi trường. GPS phía client vẫn chỉ là đầu vào, không phải bằng chứng khách thật sự đứng tại POI.
